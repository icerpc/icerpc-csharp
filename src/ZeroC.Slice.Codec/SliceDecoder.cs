// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ZeroC.Slice.Codec.Internal;

namespace ZeroC.Slice.Codec;

/// <summary>Provides methods to decode data encoded with Slice.</summary>
public ref partial struct SliceDecoder
{
    /// <summary>Gets the number of bytes decoded in the underlying buffer.</summary>
    public readonly long Consumed => _reader.Consumed;

    /// <summary>Gets the decoding context.</summary>
    /// <remarks>The decoding context is a kind of cookie: the code that creates the decoder can store this context in
    /// the decoder for later retrieval.</remarks>
    public object? DecodingContext { get; }

    /// <summary>Gets a value indicating whether this decoder has reached the end of its underlying buffer.</summary>
    /// <value><see langword="true" /> when this decoder has reached the end of its underlying buffer; otherwise
    /// <see langword="false" />.</value>
    public readonly bool End => _reader.End;

    /// <summary>Gets the number of bytes remaining in the underlying buffer.</summary>
    /// <value>The number of bytes remaining in the underlying buffer.</value>
    public readonly long Remaining => _reader.Remaining;

    private const string EndOfBufferMessage = "Attempting to decode past the end of the Slice decoder buffer.";

    private static readonly UTF8Encoding _utf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true); // no BOM

    // The number of bytes already allocated for strings, dictionaries, and sequences.
    private int _currentCollectionAllocation;

    // The maximum number of bytes that can be allocated for strings, dictionaries, and sequences.
    private readonly int _maxCollectionAllocation;

    // The sequence reader.
    private SequenceReader<byte> _reader;

    /// <summary>Constructs a new Slice decoder over a byte buffer.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="decodingContext">The decoding context.</param>
    /// <param name="maxCollectionAllocation">The maximum cumulative allocation in bytes when decoding strings,
    /// sequences, and dictionaries from this buffer.<c>-1</c> (the default) is equivalent to 8 times the buffer
    /// length, clamped to <see cref="int.MaxValue" />.</param>
    public SliceDecoder(ReadOnlySequence<byte> buffer, object? decodingContext = null, int maxCollectionAllocation = -1)
    {
        DecodingContext = decodingContext;

        _currentCollectionAllocation = 0;
        _maxCollectionAllocation = maxCollectionAllocation == -1 ? (int)Math.Min(int.MaxValue, 8L * buffer.Length) :
            (maxCollectionAllocation >= 0 ? maxCollectionAllocation :
                throw new ArgumentException(
                    $"The {nameof(maxCollectionAllocation)} argument must be greater than or equal to -1.",
                    nameof(maxCollectionAllocation)));

        _reader = new SequenceReader<byte>(buffer);
    }

    /// <summary>Constructs a new Slice decoder over a byte buffer.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="decodingContext">The decoding context.</param>
    /// <param name="maxCollectionAllocation">The maximum cumulative allocation in bytes when decoding strings,
    /// sequences, and dictionaries from this buffer.<c>-1</c> (the default) is equivalent to 8 times the buffer
    /// length, clamped to <see cref="int.MaxValue" />.</param>
    public SliceDecoder(ReadOnlyMemory<byte> buffer, object? decodingContext = null, int maxCollectionAllocation = -1)
        : this(new ReadOnlySequence<byte>(buffer), decodingContext, maxCollectionAllocation)
    {
    }

    // Decode methods for basic types

    /// <summary>Checks if the in memory representation of the bool value is valid according to the Slice encoding.</summary>
    /// <param name="value">The value to check.</param>
    /// <exception cref="InvalidDataException">If the value is out of the bool type accepted range.</exception>
    public static void CheckBoolValue(bool value)
    {
        if (Unsafe.As<bool, byte>(ref value) > 1)
        {
            throw new InvalidDataException("The value is out of the bool type accepted range.");
        }
    }

    /// <summary>Decodes a slice bool into a bool.</summary>
    /// <returns>The bool decoded by this decoder.</returns>
    public bool DecodeBool()
    {
        if (_reader.TryRead(out byte value))
        {
            return value switch
            {
                0 => false,
                1 => true,
                _ => throw new InvalidDataException("The value is out of the bool type accepted range.")
            };
        }
        else
        {
            throw new InvalidDataException(EndOfBufferMessage);
        }
    }

    /// <summary>Decodes a Slice float32 into a float.</summary>
    /// <returns>The float decoded by this decoder.</returns>
    public float DecodeFloat32() =>
        SequenceMarshal.TryRead(ref _reader, out float value) ?
            value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes a Slice float64 into a double.</summary>
    /// <returns>The double decoded by this decoder.</returns>
    public double DecodeFloat64() =>
        SequenceMarshal.TryRead(ref _reader, out double value) ?
            value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes a Slice int8 into an sbyte.</summary>
    /// <returns>The sbyte decoded by this decoder.</returns>
    public sbyte DecodeInt8() => (sbyte)DecodeUInt8();

    /// <summary>Decodes a Slice int16 into a short.</summary>
    /// <returns>The short decoded by this decoder.</returns>
    public short DecodeInt16() =>
        SequenceMarshal.TryRead(ref _reader, out short value) ?
            value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes a Slice int32 into an int.</summary>
    /// <returns>The int decoded by this decoder.</returns>
    public int DecodeInt32() =>
        SequenceMarshal.TryRead(ref _reader, out int value) ?
            value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes a Slice int64 into a long.</summary>
    /// <returns>The long decoded by this decoder.</returns>
    public long DecodeInt64() =>
        SequenceMarshal.TryRead(ref _reader, out long value) ?
            value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes a size encoded on a variable number of bytes.</summary>
    /// <returns>The size decoded by this decoder.</returns>
    public int DecodeSize()
    {
        try
        {
            return checked((int)DecodeVarUInt62());
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Cannot decode size larger than int.MaxValue.", exception);
        }
    }

    /// <summary>Decodes a Slice string into a string.</summary>
    /// <returns>The string decoded by this decoder.</returns>
    public string DecodeString()
    {
        int size = DecodeSize();
        if (size == 0)
        {
            return "";
        }
        else
        {
            // In the worst-case scenario, each byte becomes a new character. We'll adjust this allocation increase
            // after decoding the string.
            IncreaseCollectionAllocation(size, Unsafe.SizeOf<char>());

            string result;
            if (_reader.UnreadSpan.Length >= size)
            {
                try
                {
                    result = _utf8.GetString(_reader.UnreadSpan[0..size]);
                }
                catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException)
                {
                    // The two exceptions that can be thrown by GetString are ArgumentException and
                    // DecoderFallbackException. Both of which are a result of malformed data. As such, we can just
                    // throw an InvalidDataException.
                    throw new InvalidDataException("Invalid UTF-8 string.", exception);
                }
            }
            else
            {
                ReadOnlySequence<byte> bytes = _reader.UnreadSequence;
                if (size > bytes.Length)
                {
                    throw new InvalidDataException(EndOfBufferMessage);
                }
                try
                {
                    result = _utf8.GetString(bytes.Slice(0, size));
                }
                catch (Exception exception) when (exception is ArgumentException or DecoderFallbackException)
                {
                    // The two exceptions that can be thrown by GetString are ArgumentException and
                    // DecoderFallbackException. Both of which are a result of malformed data. As such, we can just
                    // throw an InvalidDataException.
                    throw new InvalidDataException("Invalid UTF-8 string.", exception);
                }
            }

            _reader.Advance(size);

            // Make the adjustment. The overall increase in allocation is result.Length * SizeOf<char>().
            DecreaseCollectionAllocation(size - result.Length, Unsafe.SizeOf<char>());
            return result;
        }
    }

    /// <summary>Decodes a Slice uint8 into a byte.</summary>
    /// <returns>The byte decoded by this decoder.</returns>
    public byte DecodeUInt8() =>
        _reader.TryRead(out byte value) ? value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes a Slice uint16 into a ushort.</summary>
    /// <returns>The ushort decoded by this decoder.</returns>
    public ushort DecodeUInt16() =>
        SequenceMarshal.TryRead(ref _reader, out ushort value) ?
            value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes a Slice uint32 into a uint.</summary>
    /// <returns>The uint decoded by this decoder.</returns>
    public uint DecodeUInt32() =>
        SequenceMarshal.TryRead(ref _reader, out uint value) ?
            value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes a Slice uint64 into a ulong.</summary>
    /// <returns>The ulong decoded by this decoder.</returns>
    public ulong DecodeUInt64() =>
        SequenceMarshal.TryRead(ref _reader, out ulong value) ?
            value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes a Slice varint32 into an int.</summary>
    /// <returns>The int decoded by this decoder.</returns>
    public int DecodeVarInt32()
    {
        try
        {
            return checked((int)DecodeVarInt62());
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("The value is out of the varint32 accepted range.", exception);
        }
    }

    /// <summary>Decodes a Slice varint62 into a long.</summary>
    /// <returns>The long decoded by this decoder.</returns>
    public long DecodeVarInt62() =>
        (PeekByte() & 0x03) switch
        {
            0 => (sbyte)DecodeUInt8() >> 2,
            1 => DecodeInt16() >> 2,
            2 => DecodeInt32() >> 2,
            _ => DecodeInt64() >> 2
        };

    /// <summary>Decodes a Slice varuint32 into a uint.</summary>
    /// <returns>The uint decoded by this decoder.</returns>
    public uint DecodeVarUInt32()
    {
        try
        {
            return checked((uint)DecodeVarUInt62());
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("The value is out of the varuint32 accepted range.", exception);
        }
    }

    /// <summary>Decodes a Slice varuint62 into a ulong.</summary>
    /// <returns>The ulong decoded by this decoder.</returns>
    public ulong DecodeVarUInt62() =>
        TryDecodeVarUInt62(out ulong value) ? value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Tries to decode a Slice uint8 into a byte.</summary>
    /// <param name="value">When this method returns <see langword="true" />, this value is set to the decoded byte.
    /// Otherwise, this value is set to its default value.</param>
    /// <returns><see langword="true" /> if the decoder is not at the end of the buffer and the decode operation
    /// succeeded; otherwise, <see langword="false" />.</returns>
    public bool TryDecodeUInt8(out byte value) => _reader.TryRead(out value);

    /// <summary>Tries to decode a Slice varuint62 into a ulong.</summary>
    /// <param name="value">When this method returns <see langword="true" />, this value is set to the decoded ulong.
    /// Otherwise, this value is set to its default value.</param>
    /// <returns><see langword="true" /> if the decoder is not at the end of the buffer and the decode operation
    /// succeeded; otherwise, <see langword="false" />.</returns>
    public bool TryDecodeVarUInt62(out ulong value)
    {
        if (_reader.TryPeek(out byte b))
        {
            switch (b & 0x03)
            {
                case 0:
                {
                    if (_reader.TryRead(out byte byteValue))
                    {
                        value = (uint)byteValue >> 2;
                        return true;
                    }
                    break;
                }
                case 1:
                {
                    if (SequenceMarshal.TryRead(ref _reader, out ushort ushortValue))
                    {
                        value = (uint)ushortValue >> 2;
                        return true;
                    }
                    break;
                }
                case 2:
                {
                    if (SequenceMarshal.TryRead(ref _reader, out uint uintValue))
                    {
                        value = uintValue >> 2;
                        return true;
                    }
                    break;
                }
                default:
                {
                    if (SequenceMarshal.TryRead(ref _reader, out ulong ulongValue))
                    {
                        value = ulongValue >> 2;
                        return true;
                    }
                    break;
                }
            }
        }
        value = 0;
        return false;
    }

    // Other methods

    /// <summary>Copy bytes from the underlying reader into the destination to fill completely destination.
    /// </summary>
    /// <param name="destination">The span to which bytes of this decoder will be copied.</param>
    /// <remarks>This method also moves the reader's Consumed property.</remarks>
    public void CopyTo(Span<byte> destination)
    {
        if (_reader.TryCopyTo(destination))
        {
            _reader.Advance(destination.Length);
        }
        else
        {
            throw new InvalidDataException(EndOfBufferMessage);
        }
    }

    /// <summary>Copy all bytes from the underlying reader into the destination buffer writer.</summary>
    /// <param name="destination">The destination buffer writer.</param>
    /// <remarks>This method also moves the reader's Consumed property.</remarks>
    public void CopyTo(IBufferWriter<byte> destination)
    {
        destination.Write(_reader.UnreadSequence);
        _reader.AdvanceToEnd();
    }

    /// <summary>Decodes a tagged field.</summary>
    /// <typeparam name="T">The type of the decoded value.</typeparam>
    /// <param name="tag">The tag.</param>
    /// <param name="decodeFunc">A decode function that decodes the value of this tagged field.</param>
    /// <returns>The decoded value of the tagged field, or <see langword="null" /> if not found.</returns>
    /// <remarks>We return a T? and not a T to avoid ambiguities in the generated code with nullable reference types
    /// such as string?.</remarks>
    public T? DecodeTagged<T>(int tag, DecodeFunc<T> decodeFunc)
    {
        int requestedTag = tag;

        while (true)
        {
            long startPos = _reader.Consumed;
            tag = DecodeVarInt32();

            if (tag == requestedTag)
            {
                // Found requested tag, so skip size:
                SkipSize();
                return decodeFunc(ref this);
            }
            else if (tag == SliceDefinitions.TagEndMarker || tag > requestedTag)
            {
                _reader.Rewind(_reader.Consumed - startPos); // rewind
                break; // while
            }
            else
            {
                Skip(DecodeSize());
                // and continue while loop
            }
        }
        return default;
    }

    /// <summary>Gets a bit sequence reader to read the underlying bit sequence later on.</summary>
    /// <param name="bitSequenceSize">The minimum number of bits in the sequence.</param>
    /// <returns>A bit sequence reader.</returns>
    public BitSequenceReader GetBitSequenceReader(int bitSequenceSize)
    {
        if (bitSequenceSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitSequenceSize),
                $"The {nameof(bitSequenceSize)} argument must be greater than 0.");
        }

        int size = SliceEncoder.GetBitSequenceByteCount(bitSequenceSize);
        ReadOnlySequence<byte> bitSequence = _reader.UnreadSequence.Slice(0, size);
        _reader.Advance(size);
        Debug.Assert(bitSequence.Length == size);
        return new BitSequenceReader(bitSequence);
    }

    /// <summary>Increases the number of bytes in the decoder's collection allocation.</summary>
    /// <param name="count">The number of elements.</param>
    /// <param name="elementSize">The size of each element in bytes.</param>
    /// <seealso cref="SliceDecoder(ReadOnlySequence{byte}, object?, int)" />
    public void IncreaseCollectionAllocation(int count, int elementSize)
    {
        Debug.Assert(count >= 0, $"{nameof(count)} must be greater than or equal to 0.");
        Debug.Assert(elementSize > 0, $"{nameof(elementSize)} must be greater than 0.");

        // Widen count to long to avoid overflow when multiplying by elementSize.
        long byteCount = (long)count * elementSize;

        int remainingAllocation = _maxCollectionAllocation - _currentCollectionAllocation;
        if (byteCount > remainingAllocation)
        {
            throw new InvalidDataException(
                $"The decoding exceeds the max collection allocation of '{_maxCollectionAllocation}'.");
        }
        _currentCollectionAllocation += (int)byteCount;
    }

    /// <summary>Skip the given number of bytes.</summary>
    /// <param name="count">The number of bytes to skip.</param>
    public void Skip(int count)
    {
        if (_reader.Remaining >= count)
        {
            _reader.Advance(count);
        }
        else
        {
            throw new InvalidDataException(EndOfBufferMessage);
        }
    }

    /// <summary>Skips the remaining tagged fields.</summary>
    public void SkipTagged()
    {
        while (true)
        {
            if (DecodeVarInt32() == SliceDefinitions.TagEndMarker)
            {
                break; // while
            }

            // Skip tagged value
            Skip(DecodeSize());
        }
    }

    /// <summary>Skip Slice size.</summary>
    public void SkipSize() => Skip(DecodeVarInt62Length(PeekByte()));

    // Applies to all var type: varint62, varuint62 etc.
    internal static int DecodeVarInt62Length(byte from) => 1 << (from & 0x03);

    /// <summary>Decreases the number of bytes in the decoder's collection allocation.</summary>
    /// <param name="count">The number of elements.</param>
    /// <param name="elementSize">The size of each element in bytes.</param>
    private void DecreaseCollectionAllocation(int count, int elementSize)
    {
        Debug.Assert(count >= 0, $"{nameof(count)} must be greater than or equal to 0.");
        Debug.Assert(elementSize > 0, $"{nameof(elementSize)} must be greater than 0.");

        // Widen count to long to avoid overflow when multiplying by elementSize.
        long byteCount = (long)count * elementSize;

        Debug.Assert(byteCount <= _currentCollectionAllocation, "Decreasing more than the current collection allocation.");
        _currentCollectionAllocation -= (int)byteCount;
    }

    private readonly byte PeekByte() =>
        _reader.TryPeek(out byte value) ? value : throw new InvalidDataException(EndOfBufferMessage);
}
