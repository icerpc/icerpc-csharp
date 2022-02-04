// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    // TODO: temporary, this should be replaced with SslStreamPipeReader, SocketPipeReader, or it should be a base
    // class to implement these.
    internal class SimpleNetworkConnectionPipeReader : PipeReader
    {
        private readonly ISimpleNetworkConnection _connection;
        private readonly Pipe _pipe;

        public override void AdvanceTo(SequencePosition consumed) =>
            _pipe.Reader.AdvanceTo(consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined) =>
            _pipe.Reader.AdvanceTo(consumed, examined);

        public override void CancelPendingRead() => _pipe.Reader.CancelPendingRead();

        public override void Complete(Exception? exception = null)
        {
            _pipe.Reader.Complete(exception);
            _pipe.Writer.Complete(exception);
        }

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancel = default)
        {
            // If there's no data available for reading on the pipe reader, we feed the pipe writer with data read
            // from the connection.
            if (!_pipe.Reader.TryRead(out ReadResult readResult))
            {
                Memory<byte> buffer = _pipe.Writer.GetMemory();
                int count = await _connection.ReadAsync(buffer, cancel).ConfigureAwait(false);
                _pipe.Writer.Advance(count);
                if (count == 0)
                {
                    await _pipe.Writer.CompleteAsync().ConfigureAwait(false);
                }
                else
                {
                    await _pipe.Writer.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
                readResult = await _pipe.Reader.ReadAsync(cancel).ConfigureAwait(false);
            }
            if (readResult.IsCanceled)
            {
                // CancelPendingRead() has been called. It's called when the connection is disposed.
                throw new ConnectionLostException();
            }
            return readResult;
        }

        public override bool TryRead(out ReadResult result) => _pipe.Reader.TryRead(out result);

        internal SimpleNetworkConnectionPipeReader(
            ISimpleNetworkConnection connection,
            MemoryPool<byte> pool,
            int minimumSegmentSize)
        {
            _connection = connection;
            _pipe = new Pipe(new PipeOptions(
                pool: pool,
                minimumSegmentSize: minimumSegmentSize,
                pauseWriterThreshold: 0,
                writerScheduler: PipeScheduler.Inline));
        }
    }
}
