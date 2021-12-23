// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Runtime.InteropServices;

namespace IceRpc.Internal
{
    /// <summary>Provides extension methods for byte buffers (such as <c>ReadOnlyMemory{byte}</c>).</summary>
    internal static class MemoryExtensions
    {
        internal static ReadOnlySpan<byte> AsReadOnlySpan(this Memory<byte> buffer) => buffer.Span;

        internal static void CopyTo(this ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, Memory<byte> destination)
        {
            int offset = 0;
            for (int i = 0; i < buffers.Length; ++i)
            {
                buffers.Span[i].CopyTo(destination[offset..]);
                offset += buffers.Span[i].Length;
            }
        }

        internal static int GetByteCount(this ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
        {
            ReadOnlySpan<ReadOnlyMemory<byte>> span = buffers.Span;

            int count = 0;
            for (int i = 0; i < span.Length; ++i)
            {
                count += span[i].Length;
            }
            return count;
        }

        internal static IList<ArraySegment<byte>> ToSegmentList(this ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
        {
            var segments = new ArraySegment<byte>[buffers.Length];
            for (int i = 0; i < buffers.Length; ++i)
            {
                if (MemoryMarshal.TryGetArray(buffers.Span[i], out ArraySegment<byte> segment))
                {
                    segments[i] = segment;
                }
                else
                {
                    throw new ArgumentException($"{nameof(buffers)} are not backed by arrays", nameof(buffers));
                }
            }
            return segments;
        }
    }
}
