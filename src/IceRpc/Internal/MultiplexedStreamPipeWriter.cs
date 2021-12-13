// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
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

                // TODO: the relevant PipeOptions should be supplied to the MultiplexedStreamPipeWriter constructor.
                _pipe ??= new Pipe();
                return _pipe.Writer;
            }
        }

        private bool _endStreamWritten; // true once endStream is written
        private bool _isReaderCompleted;
        private bool _isWriterCompleted;

        private Pipe? _pipe;

        private readonly IMultiplexedStream _stream;

        public override void Advance(int bytes) => PipeWriter.Advance(bytes);

        public override void CancelPendingFlush() => throw new NotImplementedException();

        public override void Complete(Exception? exception)
        {
            if (exception == null && !_endStreamWritten)
            {
                // no-op Complete. WriteEndStreamAndCompleteAsync should be called later.
            }
            else
            {
                _isWriterCompleted = true;

                if (_pipe != null)
                {
                    _pipe.Writer.Complete();
                    _pipe.Reader.Complete();
                    _pipe = null;
                }

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

        // Use the default CompleteAsync(Exception? exception = default) that calls Complete above.

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) =>
            FlushAsyncCore(endStream: false, cancellationToken);

        public override Memory<byte> GetMemory(int sizeHint) => PipeWriter.GetMemory(sizeHint);
        public override Span<byte> GetSpan(int sizeHint) => PipeWriter.GetSpan(sizeHint);

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

        /// <summary>Writes the endStream marker and completes this pipe writer.</summary>
        /// <remarks>We cannot simply have a WriteEndStreamAsync to be called before Complete because StreamPipeWriter
        /// over a DeflateStream writes bytes in Complete/CompleteAsync.</remarks>
        public async ValueTask WriteEndStreamAndCompleteAsync(CancellationToken cancel)
        {
            try
            {
                _ = await FlushAsyncCore(endStream: true, cancel).ConfigureAwait(false);

                if (!_isReaderCompleted && !_endStreamWritten) // flush didn't write anything
                {
                    try
                    {
                        // Write an empty buffer with endStream.
                        await _stream.WriteAsync(
                            default,
                            endStream: true,
                            cancel).ConfigureAwait(false);
                    }
                    catch (MultiplexedStreamAbortedException)
                    {
                        // TODO: confirm this is indeed correct; should we rethrow?
                        _isReaderCompleted = true;
                        throw;
                    }
                    finally
                    {
                        _endStreamWritten = true;
                    }
                }
            }
            catch (Exception ex)
            {
                await CompleteAsync(ex).ConfigureAwait(false);
                throw;
            }
            finally
            {
                _isWriterCompleted = true;
            }
        }

        // We use the default implementation for protected CopyFromAsync(Stream, CancellationToken). This default
        // implementation calls GetMemory / Advance.

        internal MultiplexedStreamPipeWriter(IMultiplexedStream stream) => _stream = stream;

        private async ValueTask<FlushResult> FlushAsyncCore(bool endStream, CancellationToken cancellationToken)
        {
            ThrowIfCompleted();

            if (!_isReaderCompleted)
            {
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
                                _endStreamWritten = true;
                            }

                            // The unflushed bytes are all consumed no matter what.
                            pipe.Reader.AdvanceTo(result.Buffer.End);
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
