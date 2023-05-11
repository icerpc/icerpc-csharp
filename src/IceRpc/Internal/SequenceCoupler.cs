// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;

namespace IceRpc.Internal;

/// <summary>Represents a coupler that connects two <see cref="ReadOnlySequence{T} "/> of byte to form a single
/// sequence.</summary>
/// <remarks>This class does not copy the memory of the sequences it connects. It does however create a
/// ReadOnlySequenceSegment instance for each segment of the input sequences, so it's not ultra cheap. If performance is
/// a concern, you should reuse the same sequence coupler over and over as it reuses the ReadOnlySequenceSegment
/// instances it creates.</remarks>
internal sealed class SequenceCoupler
{
    private readonly Segment _head = new();

    internal ReadOnlySequence<byte> Connect(ReadOnlySequence<byte> first, ReadOnlySequence<byte> second)
    {
        if (first.IsEmpty)
        {
            return second;
        }
        if (second.IsEmpty)
        {
            return first;
        }

        Segment? tail = null;
        Segment next = _head;
        long runningIndex = 0;

        Append(first);
        Append(second);

        Debug.Assert(tail is not null);
        return new ReadOnlySequence<byte>(_head, startIndex: 0, tail, endIndex: tail.Memory.Length);

        void Append(ReadOnlySequence<byte> sequence)
        {
            foreach (ReadOnlyMemory<byte> memory in sequence)
            {
                tail = next;
                tail.Reset(memory, runningIndex);
                runningIndex += memory.Length;

                // We always get (and possibly create) one extra segment. It's not used if we've reached the last
                // segment of second.
                next = tail.GetNext();
            }
        }
    }

    private class Segment : ReadOnlySequenceSegment<byte>
    {
        // GetNext always returns the next segment, and creates one if needed.
        // Note that we never clean-up these segments.
        internal Segment GetNext()
        {
            if (Next is not Segment next)
            {
                next = new Segment();
                Next = next;
            }
            return next;
        }

        internal void Reset(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            Memory = memory;
            RunningIndex = runningIndex;
        }
    }
}
