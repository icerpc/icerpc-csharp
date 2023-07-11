// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace Slice.Internal;

/// <summary>This class provides extension methods for <see cref="IBufferWriter{T}" />.</summary>
internal static class BufferWriterExtensions
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
