// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Buffers;
using System.Text;

namespace IceRpc.Slice
{
    /// <summary>Presents one or two <see cref="Span{T}"/> of bytes as a continuous sequence of bits.</summary>
    // BitSequence must be a ref struct as it holds two Span<byte>, and Span<T> is a ref struct.
    public readonly ref struct BitSequence
    {
        /// <summary>The first underlying span of bytes.</summary>
        public readonly Span<byte> FirstSpan;

        /// <summary>Gets or sets a bit in the bit sequence.</summary>
        /// <param name="index">The index of the bit to get or set. Must be between 0 and Length - 1.</param>
        /// <value>True when the bit is set, false when the bit is unset.</value>
        public bool this[int index]
        {
            get => (GetByteAsSpan(index)[0] & (1 << (index & 0x7))) != 0;

            set
            {
                Span<byte> span = GetByteAsSpan(index);
                if (value)
                {
                    span[0] = (byte)(span[0] | (1 << (index & 0x7)));
                }
                else
                {
                    span[0] = (byte)(span[0] & ~(1 << (index & 0x7)));
                }
            }
        }

        /// <summary>The length of the bit sequence.</summary>
        public int Length => (FirstSpan.Length + SecondSpan.Length) * 8;

        /// <summary>The second underlying span of bytes.</summary>
        public readonly Span<byte> SecondSpan;

        /// <summary>Constructs a bit sequence over one or two spans of bytes.</summary>
        public BitSequence(Span<byte> firstSpan, Span<byte> secondSpan = default)
        {
            FirstSpan = firstSpan;
            SecondSpan = secondSpan;
        }

        private Span<byte> GetByteAsSpan(int index)
        {
            int byteIndex = index >> 3;
            try
            {
                return byteIndex < FirstSpan.Length ? FirstSpan.Slice(byteIndex, 1) :
                    SecondSpan.Slice(byteIndex - FirstSpan.Length, 1);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new IndexOutOfRangeException($"{index} is outside the range 0..{Length - 1}");
            }
        }
    }

    /// <summary>A reader for a bit sequence.</summary>
    public ref struct BitSequenceReader
    {
        private byte _currentByte;
        private int _index; // between 0 and 7
        private SequenceReader<byte> _sequenceReader;

        /// <summary>Constructs a bit sequence reader over a bit sequence.</summary>
        /// <param name="bitSequence">The bit sequence, as a buffer of bytes.</param>
        public BitSequenceReader(ReadOnlySequence<byte> bitSequence)
        {
            _sequenceReader = new SequenceReader<byte>(bitSequence);
            _index = 0;
            if (!_sequenceReader.TryRead(out _currentByte))
            {
                throw new ArgumentException(
                    "cannot create a bit sequence reader over an empty byte sequence",
                    nameof(bitSequence));
            }
        }

        /// <summary>Reads the next bit in the bit sequence.</summary>
        /// <returns>True when the next bit is set; otherwise, false.</returns>
        public bool Read()
        {
            if (_index == 8)
            {
                _index = 0;
                if (!_sequenceReader.TryRead(out _currentByte))
                {
                    throw new InvalidOperationException("attempting to read past the end of the bit sequence");
                }
            }
            return (_currentByte & (1 << _index++)) != 0;
        }
    }
}
