// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.Diagnostics;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Implements a PipeWriter over a multiplexed stream.</summary>
    // TODO: replace by transport-specific SlicPipeWriter/QuicPipeWriter etc. implementations.
    internal class MultiplexedStreamPipeWriter : PipeWriter
    {
        public override bool CanGetUnflushedBytes => PipeWriter.CanGetUnflushedBytes;
        public override long UnflushedBytes => PipeWriter.UnflushedBytes;

        private PipeWriter PipeWriter
        {
            get
            {
                ThrowIfCompleted();

                // TODO: the relevant PipeOptions should be supplied to the constructor.
                _pipe ??= new Pipe();
                return _pipe.Writer;
            }
        }

        private bool _isReaderCompleted;
        private bool _isWriterCompleted;

        private Pipe? _pipe;

        private readonly IMultiplexedStream _stream;

        public override void Advance(int bytes) => PipeWriter.Advance(bytes);

        public override void CancelPendingFlush() => throw new NotImplementedException();

        public override void Complete(Exception? exception)
        {
#pragma warning disable CA2012
            // TODO: not very nice - can we do better? Called by the default PipeWriter.AsStream implementation.
            ValueTask valueTask = CompleteAsync(exception);
            if (!valueTask.IsCompleted)
            {
                valueTask.AsTask().GetAwaiter().GetResult();
            }
#pragma warning restore CA2012
        }

        public override async ValueTask CompleteAsync(Exception? exception = default)
        {
            try
            {
                if (exception == null)
                {
                    if (!_isReaderCompleted && !_isWriterCompleted)
                    {
                        // TODO: all this activity during CompleteAsync is worrying.

                        await WriteBufferedMemoryAsync(CancellationToken.None).ConfigureAwait(false);

                        try
                        {
                            await _stream.WriteAsync(
                                ReadOnlyMemory<ReadOnlyMemory<byte>>.Empty,
                                endStream: true,
                                cancel: default).ConfigureAwait(false);
                        }
                        catch (MultiplexedStreamAbortedException)
                        {
                            // See WriteAsync
                            _isReaderCompleted = true;
                        }
                    }
                    // else no-op
                }
                else
                {
                    byte errorCode;
                    if (exception is MultiplexedStreamAbortedException multiplexedException)
                    {
                        errorCode = multiplexedException.ErrorCode;
                    }
                    else if (exception is OperationCanceledException)
                    {
                        // TODO: it can also be InvocationCanceled, but I could find no way to identify if this stream
                        // was initiated locally (invocation) or remotely (dispatch).
                        errorCode = (byte)MultiplexedStreamError.DispatchCanceled;
                    }
                    else
                    {
                        // TODO: error code for other exceptions
                        Console.WriteLine($"MultiplexedStreamPipeWriter.CompleteAsync received {exception}");
                        errorCode = (byte)123;
                    }

                    _stream.AbortWrite(errorCode);
                }
            }
            finally
            {
                _isWriterCompleted = true;

                if (_pipe != null)
                {
                    await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
                    await _pipe.Reader.CompleteAsync().ConfigureAwait(false);
                    _pipe = null;
                }
            }
        }

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken)
        {
            ThrowIfCompleted();

            if (!_isReaderCompleted)
            {
                await WriteBufferedMemoryAsync(cancellationToken).ConfigureAwait(false);
            }

            return new FlushResult(isCanceled: false, isCompleted: _isReaderCompleted);
        }

        public override Memory<byte> GetMemory(int sizeHint) => PipeWriter.GetMemory(sizeHint);
        public override Span<byte> GetSpan(int sizeHint) => PipeWriter.GetSpan(sizeHint);

        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken)
        {
            ThrowIfCompleted();

            if (source.Length == 0)
            {
                _isWriterCompleted = true;
            }

            if (!_isReaderCompleted)
            {
                await WriteBufferedMemoryAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    await _stream.WriteAsync(
                        new ReadOnlyMemory<byte>[] { source },
                        endStream: _isWriterCompleted,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (MultiplexedStreamAbortedException)
                {
                    // TODO: confirm this is indeed correct
                    _isReaderCompleted = true;
                }
            }

            return new FlushResult(isCanceled: false, isCompleted: _isReaderCompleted);
        }

        // TODO: temporary implementation, not needed when GetMemory/AdvanceTo are implemented
        // protected override Task CopyFromAsync(Stream source, CancellationToken cancellationToken) =>
        //    CopyFromAsyncCore(source, cancellationToken);

        internal async Task CopyFromAsyncCore(Stream source, CancellationToken cancellationToken)
        {
            Memory<byte> copyBuffer = new byte[4096];

            while (true)
            {
                int read = await source.ReadAsync(copyBuffer, cancellationToken).ConfigureAwait(false);

                if (read == 0)
                {
                    break;
                }
                else
                {
                    FlushResult flushResult = await WriteAsync(
                        copyBuffer[0..read],
                        cancellationToken).ConfigureAwait(false);

                    if (flushResult.IsCompleted || flushResult.IsCanceled)
                    {
                        break;
                    }
                }
            }
        }

        internal MultiplexedStreamPipeWriter(IMultiplexedStream stream) => _stream = stream;

        private async ValueTask WriteBufferedMemoryAsync(CancellationToken cancel)
        {
            Debug.Assert(!_isReaderCompleted);

            if (_pipe is Pipe pipe)
            {
                // We're flushing our own internal pipe here.
                _ = await pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);

                if (pipe.Reader.TryRead(out ReadResult result))
                {
                    try
                    {
                        if (result.Buffer.IsSingleSegment)
                        {
                            await _stream.WriteAsync(
                                new ReadOnlyMemory<byte>[] { result.Buffer.First },
                                endStream: false,
                                cancel).ConfigureAwait(false);
                        }
                        else
                        {
                            await _stream.WriteAsync(
                                new ReadOnlyMemory<byte>[] { result.Buffer.ToArray() },
                                endStream: false,
                                cancel).ConfigureAwait(false);
                        }
                    }
                    catch (MultiplexedStreamAbortedException)
                    {
                        // TODO: confirm this is indeed correct
                        _isReaderCompleted = true;
                    }
                    finally
                    {
                        // The unflushed bytes are all consumed no matter what.
                        pipe.Reader.AdvanceTo(result.Buffer.End);
                    }
                }
                // else pipe is empty, meaning there was no call to writer.Advance (fine).
            }
        }

        private void ThrowIfCompleted()
        {
            if (_isWriterCompleted)
            {
                throw new InvalidOperationException("writer is completed");
            }
        }
    }
}
