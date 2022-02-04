// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports.Internal
{
    // TODO: temporary, this should be replaced with SslStreamPipeWriter, SocketPipeWriter, etc. or it should be a base
    // class to implement them.
    internal class SimpleNetworkConnectionPipeWriter : BufferedPipeWriter
    {
        private readonly ISimpleNetworkConnection _connection;

        internal SimpleNetworkConnectionPipeWriter(
            ISimpleNetworkConnection connection,
            MemoryPool<byte> pool,
            int minimumSegmentSize) :
            base(pool, minimumSegmentSize) => _connection = connection;

        protected internal override async ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source1,
            ReadOnlySequence<byte> source2,
            bool _,
            CancellationToken cancel)
        {
            var buffers = new List<ReadOnlyMemory<byte>>();
            foreach (ReadOnlyMemory<byte> memory in source1)
            {
                buffers.Add(memory);
            }
            foreach (ReadOnlyMemory<byte> memory in source2)
            {
                buffers.Add(memory);
            }
            await _connection.WriteAsync(buffers.ToArray(), cancel).ConfigureAwait(false);
            return new FlushResult(isCanceled: false, isCompleted: true);
        }
    }
}
