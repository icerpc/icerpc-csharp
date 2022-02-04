// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.IO.Pipelines;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace IceRpc.Transports.Internal
{
    internal class SocketPipeWriter : BufferedPipeWriter
    {
        private readonly Socket _socket;
        private readonly List<ArraySegment<byte>> _segments = new();

        internal SocketPipeWriter(Socket socket, MemoryPool<byte> pool, int minimumSegmentSize) :
            base(pool, minimumSegmentSize) => _socket = socket;

        private protected override async ValueTask<FlushResult> WriteAsync(
            ReadOnlySequence<byte> source1,
            ReadOnlySequence<byte> source2,
            bool _,
            CancellationToken cancel)
        {
            _segments.Clear();
            Add(source1);
            Add(source2);
            await _socket.SendAsync(_segments, SocketFlags.None).WaitAsync(cancel).ConfigureAwait(false);
            return new FlushResult(isCanceled: false, isCompleted: true);

            void Add(ReadOnlySequence<byte> source)
            {
                foreach (ReadOnlyMemory<byte> memory in source)
                {
                    if (MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> segment))
                    {
                        _segments.Add(segment);
                    }
                    else
                    {
                        throw new ArgumentException($"{nameof(memory)} is not backed by array", nameof(memory));
                    }
                }
            }
        }
    }
}
