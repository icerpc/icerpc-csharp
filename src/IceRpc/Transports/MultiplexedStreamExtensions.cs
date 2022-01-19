// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Transports
{
    // TODO: XXX: Remove these extensions
    internal static class MultiplexedStreamExtensions
    {
        internal static async ValueTask ReadUntilFullAsync(
            this IMultiplexedStream stream,
            Memory<byte> buffer,
            CancellationToken cancel)
        {
            ReadResult result = await stream.Input.ReadAtLeastAsync(buffer.Length, cancel).ConfigureAwait(false);
            int offset = 0;
            foreach (ReadOnlyMemory<byte> memory in result.Buffer)
            {
                int size = Math.Min(buffer.Length - offset, memory.Length);
                memory[0..size].CopyTo(buffer[offset..]);
                offset += size;
                if (offset == buffer.Length)
                {
                    break;
                }
            }
            stream.Input.AdvanceTo(result.Buffer.GetPosition(buffer.Length));
        }
    }
}
