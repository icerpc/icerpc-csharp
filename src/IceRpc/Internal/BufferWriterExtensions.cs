// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace IceRpc.Internal;

/// <summary>Provides an extension method for <see cref="IBufferWriter{T}" /> to write a <see cref="ReadOnlySequence{T}"
/// />.</summary>
internal static class BufferWriterExtensions
{
    /// <summary>Extension methods for <see cref="IBufferWriter{T}" />.</summary>
    /// <param name="writer">The buffer writer.</param>
    extension(IBufferWriter<byte> writer)
    {
        /// <summary>Writes a sequence of bytes to a buffer writer.</summary>
        internal void Write(ReadOnlySequence<byte> sequence)
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
