// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Net.Sockets;

namespace IceRpc.Transports.Internal
{
    internal class SocketPipeReader : BufferedPipeReader
    {
        private readonly Socket _socket;

        internal SocketPipeReader(Socket socket, MemoryPool<byte> pool, int minimumSegmentSize) :
            base(pool, minimumSegmentSize) => _socket = socket;

        protected internal override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancel) =>
            _socket.ReceiveAsync(buffer, SocketFlags.None, cancel);
    }
}
