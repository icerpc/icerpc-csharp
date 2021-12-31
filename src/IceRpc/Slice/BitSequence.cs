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

    /// <summary>A writer for a bit sequence.</summary>
    public ref struct BitSequenceWriter
    {
        private int _index; // the bit index in _spanEnumerator.Current

        private SpanEnumerator _spanEnumerator;

        /// <summary>Constructs a bit sequence writer over a bit sequence represented by a <see cref="SpanEnumerator"/>.
        /// </summary>
        public BitSequenceWriter(SpanEnumerator spanEnumerator)
        {
            _index = 0;
            _spanEnumerator = spanEnumerator;
            if (!_spanEnumerator.MoveNext())
            {
                throw new ArgumentException("cannot create a bit sequence writer over an empty bit sequence");
            }
            _spanEnumerator.Current.Fill(255);
        }

        /// <summary>Writes the bit at the current position in the underlying bit sequence and moves to the next
        /// position.<summary>
        /// <param name="value"><c>true</c> to set the bit and false to unset it.</param>
        public void Write(bool value)
        {
            int byteIndex = _index >> 3;

            if (byteIndex > _spanEnumerator.Current.Length)
            {
                if (!_spanEnumerator.MoveNext())
                {
                    throw new InvalidOperationException("cannot write past end of bit sequence");
                }
                _spanEnumerator.Current.Fill(255);

                _index -= byteIndex << 3;
                byteIndex = _index >> 3;
            }

            if (!value)
            {
                // unset bit
                _spanEnumerator.Current[byteIndex] =
                    (byte)(_spanEnumerator.Current[byteIndex] & ~(1 << (_index & 0x7)));
            }

            _index++;
        }
    }

    /// <summary>An enumerator over one or more <see cref="Span{T}"/> of bytes.</summary>
    public ref struct SpanEnumerator
    {
        /// <summary>Returns the current span.</summary>
        public Span<byte> Current =>
            _position switch
            {
                -1 => throw new InvalidOperationException(),
                0 => _firstSpan,
                1 => _secondSpan,
                _ => _additionalMemory![_position - 2].Span
            };

        private readonly Span<byte> _firstSpan;
        private readonly Span<byte> _secondSpan;
        private readonly IList<Memory<byte>>? _additionalMemory;
        private int _position = -1;

        /// <summary>Moves to the next span.</summary>
        /// <returns><c>true</c> when the operation was successful, and <c>false</c> when the current span is the last
        /// span.</returns>
        public bool MoveNext()
        {
            switch (_position)
            {
                case -1:
                    _position = 0;
                    return true;
                case 0:
                    if (_secondSpan.Length > 0)
                    {
                        _position = 1;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                default:
                    if (_additionalMemory != null && _additionalMemory.Count > _position - 3)
                    {
                        _position += 1;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
            }
        }

        /// <summary>Constructs a span enumerator.</summary>
        public SpanEnumerator(
            Span<byte> firstSpan,
            Span<byte> secondSpan = default,
            IList<Memory<byte>>? additionalMemory = null)
        {
            _firstSpan = firstSpan;
            _secondSpan = secondSpan;
            _additionalMemory = additionalMemory;
        }
    }
}
