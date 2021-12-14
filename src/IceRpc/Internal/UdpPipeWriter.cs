// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Implements a PipeWriter over a UDP simple network connection. This pipe writer represents a single
    /// ice1 request and only writes to the underlying connection in <see cref="CompleteAsync"/>.</summary>
    internal class UdpPipeWriter : BufferedPipeWriter
    {
        private readonly ISimpleNetworkConnection _connection;

        private bool _isWriterCompleted;

        public override void CancelPendingFlush() => throw new NotImplementedException();

        public override void Complete(Exception? exception = null)
        {
            if (exception == null)
            {
                // no-op
            }
            else
            {
                _isWriterCompleted = true;
                base.Complete(exception);
            }
        }

        /// <summary>Writes all the unflushed buffers in one shot. </summary>
        public override async ValueTask CompleteAsync(Exception? exception = null)
        {
            if (exception == null)
            {
                if (!_isWriterCompleted)
                {
                    try
                    {
                        if (PipeReader is PipeReader pipeReader)
                        {
                            await FlushWriterAsync().ConfigureAwait(false);

                            // Write everything now in one shot
                            if (pipeReader.TryRead(out ReadResult result))
                            {
                                try
                                {
                                    if (result.Buffer.IsSingleSegment)
                                    {
                                        await _connection.WriteAsync(
                                            new ReadOnlyMemory<byte>[] { result.Buffer.First },
                                            CompleteCancellationToken).ConfigureAwait(false);
                                    }
                                    else
                                    {
                                        await _connection.WriteAsync(
                                            new ReadOnlyMemory<byte>[] { result.Buffer.ToArray() },
                                            CompleteCancellationToken).ConfigureAwait(false);
                                    }
                                }
                                finally
                                {
                                    // The unflushed bytes are all consumed no matter what.
                                    pipeReader.AdvanceTo(result.Buffer.End);
                                }
                            }
                        }
                        _isWriterCompleted = true;
                        Complete();
                    }
                    catch (Exception ex)
                    {
                        Complete(ex);
                    }
                }
            }
            else
            {
                Complete(exception);
            }
        }

        public override Task CopyFromAsync(
            PipeReader source,
            bool completeWhenDone,
            CancellationToken cancel) => throw new NotImplementedException();

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken)
        {
            // no-op
            ThrowIfCompleted();
            return new(new FlushResult(isCanceled: false, isCompleted: false));
        }

        public override ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken)
        {
            ThrowIfCompleted();

            // Keeps a copy
            this.Write(source.Span);
            return new(new FlushResult(isCanceled: false, isCompleted: false));
        }

        internal UdpPipeWriter(ISimpleNetworkConnection connection) => _connection = connection;

        private void ThrowIfCompleted()
        {
            if (_isWriterCompleted)
            {
                throw new InvalidOperationException("pipe writer is completed");
            }
        }
    }
}
