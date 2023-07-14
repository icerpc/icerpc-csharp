// Copyright (c) ZeroC, Inc.

using System.Buffers;
using ZeroC.Slice.Internal;

namespace ZeroC.Slice;

/// <summary>Provides a method for reading a bit sequence.</summary>
/// <seealso href="https://docs.testing.zeroc.com/slice/encoding/encoding-only-constructs?encoding=Slice2#bit-sequence"/>
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
                "Cannot create a bit sequence reader over an empty byte sequence.",
                nameof(bitSequence));
        }
    }

    /// <summary>Reads the next bit in the bit sequence.</summary>
    /// <returns><see langword="true" /> when the next bit is set; otherwise, <see langword="false" />.</returns>
    public bool Read()
    {
        if (_index == 8)
        {
            _index = 0;
            if (!_sequenceReader.TryRead(out _currentByte))
            {
                throw new InvalidOperationException("Attempting to read past the end of the bit sequence.");
            }
        }
        return (_currentByte & (1 << _index++)) != 0;
    }
}

/// <summary>Provides a method for writing a bit sequence.</summary>
/// <seealso href="https://docs.testing.zeroc.com/slice/encoding/encoding-only-constructs?encoding=Slice2#bit-sequence"/>
public ref struct BitSequenceWriter
{
    private int _index; // the bit index in _spanEnumerator.Current

    private SpanEnumerator _spanEnumerator;

    /// <summary>Writes the bit at the current position in the underlying bit sequence and moves to the next
    /// position.</summary>
    /// <param name="value"><see langword="true" /> to set the bit and <see langword="false" /> to unset it.</param>
    public void Write(bool value)
    {
        int byteIndex = _index >> 3; // right-shift by 3 positions, equivalent to divide by 8
        Span<byte> span = _spanEnumerator.Current;

        if (byteIndex >= span.Length)
        {
            if (_spanEnumerator.MoveNext())
            {
                span = _spanEnumerator.Current;
                span.Clear();

                _index = 0;
                byteIndex = 0;
            }
            else
            {
                throw new InvalidOperationException("Cannot write past the end of the bit sequence.");
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

    /// <summary>Constructs a bit sequence writer over a bit sequence represented by a <see cref="SpanEnumerator" />.
    /// </summary>
    internal BitSequenceWriter(
        Span<byte> firstSpan,
        Span<byte> secondSpan = default,
        IList<Memory<byte>>? additionalMemory = null)
    {
        _index = 0;
        _spanEnumerator = new SpanEnumerator(firstSpan, secondSpan, additionalMemory);
        _spanEnumerator.MoveNext();

        // We fill the span with 0s, this way we only need to set bits, never unset them.
        _spanEnumerator.Current.Clear();
    }
}
