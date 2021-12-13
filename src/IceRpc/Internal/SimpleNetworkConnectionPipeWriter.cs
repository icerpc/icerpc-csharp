// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Implements a PipeWriter over a simple network connection. This is a pipe writer for a single request
    /// or response.</summary>
    internal class SimpleNetworkConnectionPipeWriter : PipeWriter
    {
        // TODO: consolidate PipeWrite helper code with MultiplexedStreamPipeWriter with base class?

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

        private readonly ISimpleNetworkConnection _connection;
        private bool _isReaderCompleted;
        private bool _isWriterCompleted;

        private Pipe? _pipe;

        public override void Advance(int bytes) => PipeWriter.Advance(bytes);

        public override void CancelPendingFlush() => throw new NotImplementedException();

        public override void Complete(Exception? _ = null)
        {
            _isWriterCompleted = true;

            if (_pipe != null)
            {
                _pipe.Writer.Complete();
                _pipe.Reader.Complete();
                _pipe = null;
            }
        }

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken)
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
                                await _connection.WriteAsync(
                                    new ReadOnlyMemory<byte>[] { result.Buffer.First },
                                    cancellationToken).ConfigureAwait(false);
                            }
                            else
                            {
                                await _connection.WriteAsync(
                                    new ReadOnlyMemory<byte>[] { result.Buffer.ToArray() },
                                    cancellationToken).ConfigureAwait(false);
                            }
                        }
                        catch
                        {
                            _isReaderCompleted = true;
                            throw;
                        }
                        finally
                        {
                            // The unflushed bytes are all consumed no matter what.
                            pipe.Reader.AdvanceTo(result.Buffer.End);
                        }
                    }
                }
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

            if (!(await FlushAsync(cancellationToken).ConfigureAwait(false)).IsCompleted)
            {
                try
                {
                    await _connection.WriteAsync(
                        new ReadOnlyMemory<byte>[] { source },
                        cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    _isReaderCompleted = true;
                    throw;
                }
            }

            return new FlushResult(isCanceled: false, isCompleted: _isReaderCompleted);
        }

        internal SimpleNetworkConnectionPipeWriter(ISimpleNetworkConnection connection) =>
            _connection = connection;

        private void ThrowIfCompleted()
        {
            if (_isWriterCompleted)
            {
                throw new InvalidOperationException("pipe writer is completed");
            }
        }
    }
}
