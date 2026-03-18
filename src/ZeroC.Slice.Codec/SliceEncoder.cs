// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace ZeroC.Slice.Codec;

/// <summary>Provides methods to encode data with Slice.</summary>
public ref partial struct SliceEncoder
{
    /// <summary>Gets the number of bytes encoded by this encoder into the underlying buffer writer.</summary>
    public int EncodedByteCount { get; private set; }

    internal const long VarInt62MinValue = -2_305_843_009_213_693_952; // -2^61
    internal const long VarInt62MaxValue = 2_305_843_009_213_693_951; // 2^61 - 1
    internal const ulong VarUInt62MinValue = 0;
    internal const ulong VarUInt62MaxValue = 4_611_686_018_427_387_903; // 2^62 - 1

    private static readonly UTF8Encoding _utf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true); // no BOM

    private readonly IBufferWriter<byte> _bufferWriter;

    private Encoder? _utf8Encoder; // initialized lazily

    /// <summary>Encodes an int as a Slice int32 into a span of 4 bytes.</summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="into">The destination byte buffer, which must be 4 bytes long.</param>
    public static void EncodeInt32(int value, Span<byte> into)
    {
        Debug.Assert(into.Length == 4);
        MemoryMarshal.Write(into, in value);
    }

    /// <summary>Encodes a ulong as a Slice varuint62 into a span of bytes using a fixed number of bytes.</summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="into">The destination byte buffer, which must be 1, 2, 4 or 8 bytes long.</param>
    public static void EncodeVarUInt62(ulong value, Span<byte> into)
    {
        int sizeLength = into.Length;
        Debug.Assert(sizeLength == 1 || sizeLength == 2 || sizeLength == 4 || sizeLength == 8);

        (uint encodedSizeExponent, long maxSize) = sizeLength switch
        {
            1 => (0x00u, 63), // 2^6 - 1
            2 => (0x01u, 16_383), // 2^14 - 1
            4 => (0x02u, 1_073_741_823), // 2^30 - 1
            _ => (0x03u, (long)VarUInt62MaxValue)
        };

        if (value > (ulong)maxSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                $"The value '{value}' cannot be encoded on {sizeLength} bytes.");
        }

        Span<byte> ulongBuf = stackalloc byte[8];
        value <<= 2;

        value |= encodedSizeExponent;
        MemoryMarshal.Write(ulongBuf, in value);
        ulongBuf[0..sizeLength].CopyTo(into);
    }

    /// <summary>Computes the minimum number of bytes required to encode a long value using the Slice encoding's
    /// variable-size encoded representation.</summary>
    /// <param name="value">The long value.</param>
    /// <returns>The minimum number of bytes required to encode <paramref name="value" />. Can be 1, 2, 4 or 8.
    /// </returns>
    public static int GetVarInt62EncodedSize(long value) => 1 << GetVarInt62EncodedSizeExponent(value);

    /// <summary>Computes the minimum number of bytes required to encode a ulong value using the Slice encoding's
    /// variable-size encoded representation.</summary>
    /// <param name="value">The ulong value.</param>
    /// <returns>The minimum number of bytes required to encode <paramref name="value" />. Can be 1, 2, 4 or 8.
    /// </returns>
    public static int GetVarUInt62EncodedSize(ulong value) => 1 << GetVarUInt62EncodedSizeExponent(value);

    /// <summary>Computes the minimum number of bytes needed to encode a variable-length size.</summary>
    /// <param name="size">The size.</param>
    /// <returns>The minimum number of bytes.</returns>
    public static int GetSizeLength(int size) => GetVarUInt62EncodedSize(checked((ulong)size));

    /// <summary>Constructs a Slice encoder.</summary>
    /// <param name="bufferWriter">A buffer writer that writes to byte buffers. See important remarks below.</param>
    /// <remarks>Warning: the Slice encoding requires rewriting buffers, and many buffer writers do not support this
    /// behavior. It is safe to use a pipe writer or a buffer writer that writes to a single fixed-size buffer (without
    /// reallocation).</remarks>
    public SliceEncoder(IBufferWriter<byte> bufferWriter)
        : this() =>
        _bufferWriter = bufferWriter;

    // Encode methods for basic types

    /// <summary>Encodes a bool into a Slice bool.</summary>
    /// <param name="v">The boolean to encode.</param>
    public void EncodeBool(bool v) => EncodeUInt8(v ? (byte)1 : (byte)0);

    /// <summary>Encodes a float into a Slice float32.</summary>
    /// <param name="v">The float to encode.</param>
    public void EncodeFloat32(float v) => EncodeFixedSizeNumeric(v);

    /// <summary>Encodes a double into a Slice float64.</summary>
    /// <param name="v">The double to encode.</param>
    public void EncodeFloat64(double v) => EncodeFixedSizeNumeric(v);

    /// <summary>Encodes an sbyte into a Slice int8.</summary>
    /// <param name="v">The sbyte to encode.</param>
    public void EncodeInt8(sbyte v) => EncodeUInt8((byte)v);

    /// <summary>Encodes a short into a Slice int16.</summary>
    /// <param name="v">The short to encode.</param>
    public void EncodeInt16(short v) => EncodeFixedSizeNumeric(v);

    /// <summary>Encodes an int into a Slice int32.</summary>
    /// <param name="v">The int to encode.</param>
    public void EncodeInt32(int v) => EncodeFixedSizeNumeric(v);

    /// <summary>Encodes a long into a Slice int64.</summary>
    /// <param name="v">The long to encode.</param>
    public void EncodeInt64(long v) => EncodeFixedSizeNumeric(v);

    /// <summary>Encodes a size on variable number of bytes.</summary>
    /// <param name="value">The size to encode.</param>
    public void EncodeSize(int value)
    {
        if (value < 0)
        {
            throw new ArgumentException(
                $"The {nameof(value)} argument must be greater than or equal to 0.",
                nameof(value));
        }

        EncodeVarUInt62((ulong)value);
    }

    /// <summary>Encodes a string into a Slice string.</summary>
    /// <param name="v">The string to encode.</param>
    public void EncodeString(string v)
    {
        if (v.Length == 0)
        {
            EncodeSize(0);
        }
        else
        {
            int maxSize = _utf8.GetMaxByteCount(v.Length);
            int sizeLength = GetSizeLength(maxSize);
            Span<byte> sizePlaceholder = GetPlaceholderSpan(sizeLength);

            Span<byte> currentSpan = _bufferWriter.GetSpan();
            if (currentSpan.Length >= maxSize)
            {
                // Encode directly into currentSpan
                int size = _utf8.GetBytes(v, currentSpan);
                EncodeVarUInt62((ulong)size, sizePlaceholder);
                Advance(size);
            }
            else
            {
                // Encode piecemeal using _utf8Encoder
                if (_utf8Encoder is null)
                {
                    _utf8Encoder = _utf8.GetEncoder();
                }
                else
                {
                    _utf8Encoder.Reset();
                }

                ReadOnlySpan<char> chars = v.AsSpan();
                _utf8Encoder.Convert(chars, _bufferWriter, flush: true, out long bytesUsed, out bool completed);

                Debug.Assert(completed); // completed is always true when flush is true
                int size = checked((int)bytesUsed);
                EncodedByteCount += size;
                EncodeVarUInt62((ulong)size, sizePlaceholder);
            }
        }
    }

    /// <summary>Encodes a byte into a Slice uint8.</summary>
    /// <param name="v">The byte to encode.</param>
    public void EncodeUInt8(byte v)
    {
        Span<byte> span = _bufferWriter.GetSpan();
        span[0] = v;
        Advance(1);
    }

    /// <summary>Encodes a ushort into a Slice uint16.</summary>
    /// <param name="v">The ushort to encode.</param>
    public void EncodeUInt16(ushort v) => EncodeFixedSizeNumeric(v);

    /// <summary>Encodes a uint into a Slice uint32.</summary>
    /// <param name="v">The uint to encode.</param>
    public void EncodeUInt32(uint v) => EncodeFixedSizeNumeric(v);

    /// <summary>Encodes a ulong into a Slice uint64.</summary>
    /// <param name="v">The ulong to encode.</param>
    public void EncodeUInt64(ulong v) => EncodeFixedSizeNumeric(v);

    /// <summary>Encodes an int into a Slice varint32.</summary>
    /// <param name="v">The int to encode.</param>
    public void EncodeVarInt32(int v) => EncodeVarInt62(v);

    /// <summary>Encodes a long into a Slice varint62, with the minimum number of bytes required
    /// by the encoding.</summary>
    /// <param name="v">The long to encode. It must be in the range [-2^61..2^61 - 1].</param>
    public void EncodeVarInt62(long v)
    {
        int encodedSizeExponent = GetVarInt62EncodedSizeExponent(v);
        v <<= 2;
        v |= (uint)encodedSizeExponent;

        Span<byte> data = _bufferWriter.GetSpan(sizeof(long));
        MemoryMarshal.Write(data, in v);
        Advance(1 << encodedSizeExponent);
    }

    /// <summary>Encodes a uint into a Slice varuint32.</summary>
    /// <param name="v">The uint to encode.</param>
    public void EncodeVarUInt32(uint v) => EncodeVarUInt62(v);

    /// <summary>Encodes a ulong into a Slice varuint62, with the minimum number of bytes
    /// required by the encoding.</summary>
    /// <param name="v">The ulong to encode. It must be in the range [0..2^62 - 1].</param>
    public void EncodeVarUInt62(ulong v)
    {
        int encodedSizeExponent = GetVarUInt62EncodedSizeExponent(v);
        v <<= 2;
        v |= (uint)encodedSizeExponent;

        Span<byte> data = _bufferWriter.GetSpan(sizeof(ulong));
        MemoryMarshal.Write(data, in v);
        Advance(1 << encodedSizeExponent);
    }

    // Other methods

    /// <summary>Encodes a non-null tagged value. The number of bytes needed to encode the value is not known before
    /// encoding this value.</summary>
    /// <typeparam name="T">The type of the value being encoded.</typeparam>
    /// <param name="tag">The tag.</param>
    /// <param name="v">The value to encode.</param>
    /// <param name="encodeAction">The delegate that encodes the value after the tag header.</param>
    public void EncodeTagged<T>(int tag, T v, EncodeAction<T> encodeAction) where T : notnull
    {
        EncodeVarInt32(tag); // the key
        Span<byte> sizePlaceholder = GetPlaceholderSpan(4);
        int startPos = EncodedByteCount;
        encodeAction(ref this, v);
        EncodeVarUInt62((ulong)(EncodedByteCount - startPos), sizePlaceholder);
    }

    /// <summary>Encodes a non-null tagged value. The number of bytes needed to encode the value is known before
    /// encoding the value.</summary>
    /// <typeparam name="T">The type of the value being encoded.</typeparam>
    /// <param name="tag">The tag.</param>
    /// <param name="size">The number of bytes needed to encode the value.</param>
    /// <param name="v">The value to encode.</param>
    /// <param name="encodeAction">The delegate that encodes the value after the tag header.</param>
    public void EncodeTagged<T>(int tag, int size, T v, EncodeAction<T> encodeAction) where T : notnull
    {
        if (size <= 0)
        {
            throw new ArgumentException("Invalid size value, size must be greater than 0.", nameof(size));
        }

        EncodeVarInt32(tag);

        EncodeSize(size);
        int startPos = EncodedByteCount;
        encodeAction(ref this, v);

        int actualSize = EncodedByteCount - startPos;
        if (actualSize != size)
        {
            throw new ArgumentException(
                $"The value of size ({size}) does not match encoded size ({actualSize}).",
                nameof(size));
        }
    }

    /// <summary>Allocates a new bit sequence in the underlying buffer(s) and returns a writer for this bit sequence.
    /// </summary>
    /// <param name="bitSequenceSize">The minimum number of bits in the bit sequence.</param>
    /// <returns>The bit sequence writer.</returns>
    public BitSequenceWriter GetBitSequenceWriter(int bitSequenceSize)
    {
        if (bitSequenceSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitSequenceSize),
                $"The {nameof(bitSequenceSize)} argument must be greater than 0.");
        }

        int remaining = GetBitSequenceByteCount(bitSequenceSize);

        Span<byte> firstSpan = _bufferWriter.GetSpan();
        Span<byte> secondSpan = default;

        // We only create this additionalMemory list in the rare situation where 2 spans are not sufficient.
        List<Memory<byte>>? additionalMemory = null;

        if (firstSpan.Length >= remaining)
        {
            firstSpan = firstSpan[0..remaining];
            Advance(remaining);
        }
        else
        {
            Advance(firstSpan.Length);
            remaining -= firstSpan.Length;

            secondSpan = _bufferWriter.GetSpan();
            if (secondSpan.Length >= remaining)
            {
                secondSpan = secondSpan[0..remaining];
                Advance(remaining);
            }
            else
            {
                Advance(secondSpan.Length);
                remaining -= secondSpan.Length;
                additionalMemory = new List<Memory<byte>>();

                do
                {
                    Memory<byte> memory = _bufferWriter.GetMemory();
                    if (memory.Length >= remaining)
                    {
                        additionalMemory.Add(memory[0..remaining]);
                        Advance(remaining);
                        remaining = 0;
                    }
                    else
                    {
                        additionalMemory.Add(memory);
                        Advance(memory.Length);
                        remaining -= memory.Length;
                    }
                }
                while (remaining > 0);
            }
        }

        return new BitSequenceWriter(firstSpan, secondSpan, additionalMemory);
    }

    /// <summary>Gets a placeholder to be filled-in later.</summary>
    /// <param name="size">The size of the placeholder, typically a small number like 4.</param>
    /// <returns>A buffer of length <paramref name="size" />.</returns>
    /// <remarks>We make the assumption the underlying buffer writer allows rewriting memory it provided even after
    /// successive calls to GetMemory/GetSpan and Advance.</remarks>
    public Span<byte> GetPlaceholderSpan(int size)
    {
        Debug.Assert(size > 0);
        Span<byte> placeholder = _bufferWriter.GetSpan(size)[0..size];
        Advance(size);
        return placeholder;
    }

    /// <summary>Copies a span of bytes to the underlying buffer writer.</summary>
    /// <param name="span">The span to copy.</param>
    public void WriteByteSpan(ReadOnlySpan<byte> span)
    {
        _bufferWriter.Write(span);
        EncodedByteCount += span.Length;
    }

    internal static int GetBitSequenceByteCount(int bitCount) => (bitCount >> 3) + ((bitCount & 0x07) != 0 ? 1 : 0);

    /// <summary>Encodes a fixed-size numeric value.</summary>
    /// <param name="v">The numeric value to encode.</param>
    internal void EncodeFixedSizeNumeric<T>(T v) where T : struct
    {
        int elementSize = Unsafe.SizeOf<T>();
        Span<byte> data = _bufferWriter.GetSpan(elementSize)[0..elementSize];
        MemoryMarshal.Write(data, in v);
        Advance(elementSize);
    }

    /// <summary>Gets a placeholder to be filled-in later.</summary>
    /// <param name="size">The size of the placeholder, typically a small number like 4.</param>
    /// <returns>A buffer of length <paramref name="size" />.</returns>
    /// <remarks>We make the assumption the underlying buffer writer allows rewriting memory it provided even after
    /// successive calls to GetMemory/GetSpan and Advance.</remarks>
    internal Memory<byte> GetPlaceholderMemory(int size)
    {
        Debug.Assert(size > 0);
        Memory<byte> placeholder = _bufferWriter.GetMemory(size)[0..size];
        Advance(size);
        return placeholder;
    }

    /// <summary>Gets the minimum number of bytes needed to encode a long value with the varint62 encoding as an
    /// exponent of 2.</summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>N where 2^N is the number of bytes needed to encode value with Slice's varint62 encoding.</returns>
    private static int GetVarInt62EncodedSizeExponent(long value)
    {
        if (value < VarInt62MinValue || value > VarInt62MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"The value '{value}' is out of the varint62 range.");
        }

        return (value << 2) switch
        {
            long b when b >= sbyte.MinValue && b <= sbyte.MaxValue => 0,
            long s when s >= short.MinValue && s <= short.MaxValue => 1,
            long i when i >= int.MinValue && i <= int.MaxValue => 2,
            _ => 3
        };
    }

    /// <summary>Gets the minimum number of bytes needed to encode a ulong value with the varuint62 encoding as an
    /// exponent of 2.</summary>
    /// <param name="value">The value to encode.</param>
    /// <returns>N where 2^N is the number of bytes needed to encode value with Slice's varuint62 encoding.</returns>
    private static int GetVarUInt62EncodedSizeExponent(ulong value)
    {
        if (value > VarUInt62MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"The value '{value}' is out of the varuint62 range.");
        }

        return (value << 2) switch
        {
            ulong b when b <= byte.MaxValue => 0,
            ulong s when s <= ushort.MaxValue => 1,
            ulong i when i <= uint.MaxValue => 2,
            _ => 3
        };
    }

    private void Advance(int count)
    {
        _bufferWriter.Advance(count);
        EncodedByteCount += count;
    }
}
