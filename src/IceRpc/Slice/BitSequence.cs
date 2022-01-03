// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Buffers;

namespace IceRpc.Slice
{
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

        /// <summary>Writes the bit at the current position in the underlying bit sequence and moves to the next
        /// position.</summary>
        /// <param name="value"><c>true</c> to set the bit and <c>false</c> to unset it.</param>
        public void Write(bool value)
        {
            int byteIndex = _index >> 3; // right-shift by 3 positions, equivalent to divide by 8
            Span<byte> span = _spanEnumerator.Current;

            if (byteIndex >= span.Length)
            {
                if (_spanEnumerator.MoveNext())
                {
                    span = _spanEnumerator.Current;
                    span.Fill(0);

                    _index = 0;
                    byteIndex = 0;
                }
                else
                {
                    throw new InvalidOperationException("cannot write past end of bit sequence");
                }
            }

            if (value)
            {
                // set bit
                span[byteIndex] = (byte)(span[byteIndex] | (1 << (_index & 0x7)));
            }
            // else keep it unset

            _index++;
        }

        /// <summary>Constructs a bit sequence writer over a bit sequence represented by a <see cref="SpanEnumerator"/>.
        /// </summary>
        internal BitSequenceWriter(SpanEnumerator spanEnumerator)
        {
            _index = 0;
            _spanEnumerator = spanEnumerator;
            if (_spanEnumerator.MoveNext())
            {
                // We fill the span with 0s, this way we only need to set bits, never unset them.
                _spanEnumerator.Current.Fill(0);
            }
            else
            {
                throw new ArgumentException("cannot create a bit sequence writer over an empty bit sequence");
            }
        }
    }
}
