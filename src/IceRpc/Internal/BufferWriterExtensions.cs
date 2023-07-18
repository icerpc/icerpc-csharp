// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace IceRpc.Internal;

/// <summary>Provides extension methods for <see cref="IBufferWriter{T}" /> to write a <see cref="ReadOnlySequence{T}"
/// />.</summary>
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
