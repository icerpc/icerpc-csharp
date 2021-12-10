// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Implements a PipeWriter over a multiplexed stream. This partial implementation should be good enough
    /// for PipeReader.CopyAsync.</summary>
    internal class MultiplexedStreamPipeWriter : PipeWriter
    {
        public override bool CanGetUnflushedBytes => false; // doesn't support unflushed bytes at all

        private bool _isReaderCompleted;
        private bool _isWriterCompleted;

        private readonly IMultiplexedStream _stream;

        public override void Advance(int bytes) => throw new NotImplementedException();

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
                    Console.WriteLine($"CompleteAsync received {exception}");

                    // TODO: error code for other exceptions?
                    byte errorCode = exception is MultiplexedStreamAbortedException multiplexedException ?
                        multiplexedException.ErrorCode : (byte)25;

                    _stream.AbortWrite(errorCode);
                }
            }
            finally
            {
                _isWriterCompleted = true;
            }
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken)
        {
            ThrowIfCompleted();
            return new(new FlushResult(isCanceled: false, isCompleted: _isReaderCompleted));
        }

        public override Memory<byte> GetMemory(int sizeHint) => throw new NotImplementedException();
        public override Span<byte> GetSpan(int sizeHint) => throw new NotImplementedException();

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
        protected override async Task CopyFromAsync(Stream source, CancellationToken cancellationToken)
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

        private void ThrowIfCompleted()
        {
            if (_isWriterCompleted)
            {
                throw new InvalidOperationException("writer is completed");
            }
        }
    }
}
