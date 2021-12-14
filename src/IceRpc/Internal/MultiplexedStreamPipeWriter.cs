// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Implements a PipeWriter over a multiplexed stream.</summary>
    // TODO: replace by transport-specific SlicPipeWriter/QuicPipeWriter etc. implementations.
    internal class MultiplexedStreamPipeWriter : BufferedPipeWriter
    {
        private bool _isReaderCompleted;
        private bool _isWriterCompleted;

        private readonly IMultiplexedStream _stream;

        public override void CancelPendingFlush() => throw new NotImplementedException();

        public override void Complete(Exception? exception)
        {
            if (exception == null)
            {
                // Ignored, no-op
                // Unfortunately this is called by System.IO.Pipelines.PipeWriterStream.Dispose as a result of a
                // stream Dispose.
            }
            else if (!_isWriterCompleted)
            {
                _isWriterCompleted = true;
                base.Complete(exception);

                if (exception != null)
                {
                    byte errorCode = exception switch
                    {
                        MultiplexedStreamAbortedException multiplexedException => multiplexedException.ErrorCode,
                        // TODO: could it also be InvocationCanceled?
                        OperationCanceledException => (byte)MultiplexedStreamError.DispatchCanceled,
                        // TODO: error code for other exceptions;
                        _ => 123
                    };

                    _stream.AbortWrite(errorCode);
                }
            }
        }

        public override async ValueTask CompleteAsync(Exception? exception = default)
        {
            if (!_isWriterCompleted)
            {
                if (exception != null)
                {
                    Complete(exception);
                }
                else
                {
                    try
                    {
                        _ = await FlushAsyncCore(endStream: true, CompleteCancellationToken).ConfigureAwait(false);
                        _isWriterCompleted = true;
                        base.Complete();
                    }
                    catch (Exception ex)
                    {
                        Complete(ex);
                        throw;
                    }
                }
            }
        }

        public override Task CopyFromAsync(
            PipeReader source,
            bool completeWhenDone,
            CancellationToken cancel) => throw new NotImplementedException();

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) =>
            FlushAsyncCore(endStream: false, cancellationToken);

        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken)
        {
            ThrowIfCompleted();

            if (!(await FlushAsyncCore(endStream: false, cancellationToken).ConfigureAwait(false)).IsCompleted)
            {
                if (source.Length > 0)
                {
                    try
                    {
                        await _stream.WriteAsync(
                            new ReadOnlyMemory<byte>[] { source },
                            endStream: false,
                            cancellationToken).ConfigureAwait(false);
                    }
                    catch (MultiplexedStreamAbortedException)
                    {
                        // TODO: confirm this is indeed correct. Should we rethrow?
                        _isReaderCompleted = true;
                    }
                }
            }

            return new FlushResult(isCanceled: false, isCompleted: _isReaderCompleted);
        }

        // We use the default implementation for protected CopyFromAsync(Stream, CancellationToken). This default
        // implementation calls GetMemory / Advance.

        internal MultiplexedStreamPipeWriter(IMultiplexedStream stream) => _stream = stream;

        private async ValueTask<FlushResult> FlushAsyncCore(bool endStream, CancellationToken cancellationToken)
        {
            ThrowIfCompleted();

            if (!_isReaderCompleted)
            {
                bool wroteEndStream = false;

                if (PipeReader is PipeReader pipeReader)
                {
                    await FlushWriterAsync().ConfigureAwait(false);

                    if (pipeReader.TryRead(out ReadResult result))
                    {
                        try
                        {
                            if (result.Buffer.IsSingleSegment)
                            {
                                await _stream.WriteAsync(
                                    new ReadOnlyMemory<byte>[] { result.Buffer.First },
                                    endStream,
                                    cancellationToken).ConfigureAwait(false);
                            }
                            else
                            {
                                await _stream.WriteAsync(
                                    new ReadOnlyMemory<byte>[] { result.Buffer.ToArray() },
                                    endStream,
                                    cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch (MultiplexedStreamAbortedException)
                        {
                            // TODO: confirm this is indeed correct; should we rethrow?
                            _isReaderCompleted = true;
                        }
                        finally
                        {
                            if (endStream)
                            {
                                wroteEndStream = true;
                            }
                            // The unflushed bytes are all consumed no matter what.
                            pipeReader.AdvanceTo(result.Buffer.End);
                        }
                    }

                    if (!_isReaderCompleted && endStream && !wroteEndStream) // the above didn't write anything
                    {
                        try
                        {
                            // Write an empty buffer with endStream.
                            await _stream.WriteAsync(
                                default,
                                endStream: true,
                                cancellationToken).ConfigureAwait(false);
                        }
                        catch (MultiplexedStreamAbortedException)
                        {
                            _isReaderCompleted = true;
                            throw;
                        }
                    }
                }
            }

            return new FlushResult(isCanceled: false, isCompleted: _isReaderCompleted);
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
