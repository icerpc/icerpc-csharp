// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;

namespace IceRpc.Slice.Internal
{
    /// <summary>Extension methods for ReadOnlySequence{byte}.</summary>
    internal static class ReadOnlySequenceExtensions
    {
        // Temporary until the IceDecoders can decode a ReadOnlySequence<byte> directly.
        internal static ReadOnlyMemory<byte> ToSingleBuffer(this ReadOnlySequence<byte> sequence)
        {
            if (sequence.IsSingleSegment)
            {
                return sequence.First;
            }
            else
            {
                Memory<byte> buffer = new byte[sequence.Length];
                sequence.CopyTo(buffer.Span);
                return buffer;
            }
        }
    }
}
