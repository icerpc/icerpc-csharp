// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Implements a PipeWriter over a simple network connection. This is a pipe writer for a single request
    /// or response.</summary>
    internal class SimpleNetworkConnectionPipeWriter : BufferedPipeWriter
    {
        private readonly ISimpleNetworkConnection _connection;
        private bool _isReaderCompleted;
        private bool _isWriterCompleted;

        public override void CancelPendingFlush() => throw new NotImplementedException();

        public override void Complete(Exception? exception = null)
        {
            _isWriterCompleted = true;
            base.Complete(exception);
        }

        public override Task CopyFromAsync(
            PipeReader source,
            bool completeWhenDone,
            CancellationToken cancel) => throw new NotImplementedException();

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken)
        {
            ThrowIfCompleted();

            if (!_isReaderCompleted)
            {
                if (PipeReader is PipeReader pipeReader)
                {
                    await FlushWriterAsync().ConfigureAwait(false);

                    if (pipeReader.TryRead(out ReadResult result))
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
                            pipeReader.AdvanceTo(result.Buffer.End);
                        }
                    }
                }
            }

            return new FlushResult(isCanceled: false, isCompleted: _isReaderCompleted);
        }
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
