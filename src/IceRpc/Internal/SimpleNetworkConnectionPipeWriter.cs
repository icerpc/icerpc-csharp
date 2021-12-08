// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using System.IO.Pipelines;

namespace IceRpc.Internal
{
    /// <summary>Implements a PipeWriter over a simple network connection. This is a pipe writer for a single request
    /// or response.</summary>
    internal class SimpleNetworkConnectionPipeWriter : PipeWriter
    {
        public override bool CanGetUnflushedBytes => false; // doesn't support unflushed bytes at all
        private readonly ISimpleNetworkConnection _connection;
        private bool _isReaderCompleted;

        public override void Advance(int bytes) => throw new NotImplementedException();

        public override void CancelPendingFlush() => throw new NotImplementedException();

        public override void Complete(Exception? _)
        {
            // no-op
        }

        public override ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken) =>
            new(new FlushResult(isCanceled: false, isCompleted: _isReaderCompleted));

        public override Memory<byte> GetMemory(int sizeHint) => throw new NotImplementedException();
        public override Span<byte> GetSpan(int sizeHint) => throw new NotImplementedException();

        public override async ValueTask<FlushResult> WriteAsync(
            ReadOnlyMemory<byte> source,
            CancellationToken cancellationToken)
        {
            if (!_isReaderCompleted)
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
    }
}
