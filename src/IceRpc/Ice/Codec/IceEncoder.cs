// Copyright (c) ZeroC, Inc.

using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static IceRpc.Ice.Codec.Internal.IceEncodingDefinitions;

namespace IceRpc.Ice.Codec;

/// <summary>Provides methods to encode data with Ice.</summary>
public ref partial struct IceEncoder
{
    /// <summary>Gets the number of bytes encoded by this encoder into the underlying buffer writer.</summary>
    public int EncodedByteCount { get; private set; }

    private static readonly UTF8Encoding _utf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true); // no BOM

    private readonly IBufferWriter<byte> _bufferWriter;

    private ClassContext _classContext;

    private Encoder? _utf8Encoder; // initialized lazily

    /// <summary>Encodes an int as an Ice int into a span of 4 bytes.</summary>
    /// <param name="value">The value to encode.</param>
    /// <param name="into">The destination byte buffer, which must be 4 bytes long.</param>
    public static void EncodeInt(int value, Span<byte> into)
    {
        Debug.Assert(into.Length == 4);
        MemoryMarshal.Write(into, in value);
    }

    /// <summary>Computes the minimum number of bytes needed to encode a variable-length size.</summary>
    /// <param name="size">The size.</param>
    /// <returns>The minimum number of bytes.</returns>
    public static int GetSizeLength(int size) => size < 255 ? 1 : 5;

    /// <summary>Constructs an Ice encoder.</summary>
    /// <param name="bufferWriter">A buffer writer that writes to byte buffers. See important remarks below.</param>
    /// <param name="classFormat">The class format.</param>
    /// <remarks>Warning: the Ice encoding requires rewriting buffers, and many buffer writers do not support this
    /// behavior. It is safe to use a pipe writer or a buffer writer that writes to a single fixed-size buffer (without
    /// reallocation).</remarks>
    public IceEncoder(IBufferWriter<byte> bufferWriter, ClassFormat classFormat = default)
        : this()
    {
        _bufferWriter = bufferWriter;
        _classContext = new ClassContext(classFormat);
    }

    // Encode methods for basic types

    /// <summary>Encodes a bool into an Ice bool.</summary>
    /// <param name="v">The boolean to encode.</param>
    public void EncodeBool(bool v) => EncodeByte(v ? (byte)1 : (byte)0);

    /// <summary>Encodes a byte into an Ice byte.</summary>
    /// <param name="v">The byte to encode.</param>
    public void EncodeByte(byte v)
    {
        Span<byte> span = _bufferWriter.GetSpan();
        span[0] = v;
        Advance(1);
    }

    /// <summary>Encodes a double into an Ice double.</summary>
    /// <param name="v">The double to encode.</param>
    public void EncodeDouble(double v) => EncodeFixedSizeNumeric(v);

    /// <summary>Encodes a float into an Ice float.</summary>
    /// <param name="v">The float to encode.</param>
    public void EncodeFloat(float v) => EncodeFixedSizeNumeric(v);

    /// <summary>Encodes an int into an Ice int.</summary>
    /// <param name="v">The int to encode.</param>
    public void EncodeInt(int v) => EncodeFixedSizeNumeric(v);

    /// <summary>Encodes a long into an Ice long.</summary>
    /// <param name="v">The long to encode.</param>
    public void EncodeLong(long v) => EncodeFixedSizeNumeric(v);

    /// <summary>Encodes a short into an Ice short.</summary>
    /// <param name="v">The short to encode.</param>
    public void EncodeShort(short v) => EncodeFixedSizeNumeric(v);

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

        if (value < 255)
        {
            EncodeByte((byte)value);
        }
        else
        {
            EncodeByte(255);
            EncodeInt(value);
        }
    }

    /// <summary>Encodes a string into an Ice string.</summary>
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
                EncodeSizeIntoPlaceholder(size, sizePlaceholder);
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
                EncodeSizeIntoPlaceholder(size, sizePlaceholder);
            }
        }

        static void EncodeSizeIntoPlaceholder(int size, Span<byte> into)
        {
            if (into.Length == 1)
            {
                Debug.Assert(size < 255);
                into[0] = (byte)size;
            }
            else
            {
                Debug.Assert(into.Length == 5);
                into[0] = 255;
                EncodeInt(size, into[1..]);
            }
        }
    }

    // Other methods

    /// <summary>Encodes a non-null tagged value. The number of bytes needed to encode the value is known before
    /// encoding the value. This method always use the VSize tag format.</summary>
    /// <typeparam name="T">The type of the value being encoded.</typeparam>
    /// <param name="tag">The tag.</param>
    /// <param name="size">The number of bytes needed to encode the value.</param>
    /// <param name="v">The value to encode.</param>
    /// <param name="encodeAction">The delegate that encodes the value after the tag header.</param>
    public void EncodeTagged<T>(int tag, int size, T v, EncodeAction<T> encodeAction) where T : notnull
    {
        if (size <= 0)
        {
            throw new ArgumentException("Invalid size value, size must be greater than zero.", nameof(size));
        }

        EncodeTaggedFieldHeader(tag, TagFormat.VSize);

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

    /// <summary>Encodes a non-null tagged value. The number of bytes needed to encode the value is not known before
    /// encoding this value.</summary>
    /// <typeparam name="T">The type of the value being encoded.</typeparam>
    /// <param name="tag">The tag. Must be either FSize or OptimizedVSize.</param>
    /// <param name="tagFormat">The tag format.</param>
    /// <param name="v">The value to encode.</param>
    /// <param name="encodeAction">The delegate that encodes the value after the tag header.</param>
    /// <exception cref="ArgumentException">Thrown if <paramref name="tagFormat" /> is VSize.</exception>
    public void EncodeTagged<T>(
        int tag,
        TagFormat tagFormat,
        T v,
        EncodeAction<T> encodeAction) where T : notnull
    {
        switch (tagFormat)
        {
            case TagFormat.F1:
            case TagFormat.F2:
            case TagFormat.F4:
            case TagFormat.F8:
            case TagFormat.Size:
                EncodeTaggedFieldHeader(tag, tagFormat);
                encodeAction(ref this, v);
                break;
            case TagFormat.FSize:
                EncodeTaggedFieldHeader(tag, tagFormat);
                Span<byte> placeholder = GetPlaceholderSpan(4);
                int startPos = EncodedByteCount;
                encodeAction(ref this, v);
                // We don't include the size-length in the size we encode.
                EncodeInt(EncodedByteCount - startPos, placeholder);
                break;

            case TagFormat.OptimizedVSize:
                // Used to encode string, and sequences of non optional elements with 1 byte min wire size,
                // in this case OptimizedVSize is always used to optimize out the size.
                EncodeTaggedFieldHeader(tag, TagFormat.VSize);
                encodeAction(ref this, v);
                break;

            default:
                throw new ArgumentException($"Invalid tag format value: '{tagFormat}'.", nameof(tagFormat));
        }
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

    private void Advance(int count)
    {
        _bufferWriter.Advance(count);
        EncodedByteCount += count;
    }

    /// <summary>Encodes the header for a tagged field.</summary>
    /// <param name="tag">The numeric tag associated with the field.</param>
    /// <param name="format">The tag format.</param>
    private void EncodeTaggedFieldHeader(int tag, TagFormat format)
    {
        Debug.Assert(format != TagFormat.OptimizedVSize); // OptimizedVSize cannot be encoded

        int v = (int)format;
        if (tag < 30)
        {
            v |= tag << 3;
            EncodeByte((byte)v);
        }
        else
        {
            v |= 0x0F0; // tag = 30
            EncodeByte((byte)v);
            EncodeSize(tag);
        }

        if (_classContext.Current.InstanceType != InstanceType.None)
        {
            _classContext.Current.SliceFlags |= SliceFlags.HasTaggedFields;
        }
    }
}
