// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;

namespace IceRpc.Internal
{
    /// <summary>This class provides extension methods for <see cref="IBufferWriter{T}"/>.</summary>
    internal static partial class BufferWriterExtensions
    {
        /// <summary>Writes a sequence of bytes to a buffer writer.</summary>
        internal static void Write(this IBufferWriter<byte> writer, ReadOnlySequence<byte> sequence)
        {
            if (sequence.IsSingleSegment)
            {
                writer.Write(sequence.FirstSpan);
            }
            else
            {
                foreach (ReadOnlyMemory<byte> segment in sequence)
                {
                    writer.Write(segment.Span);
                }
            }
        }
    }
}
