// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using static IceRpc.Slice.Internal.Slice1Definitions;

namespace IceRpc.Slice;

/// <summary>Provides methods to decode data encoded with Slice1 or Slice2.</summary>
public ref partial struct SliceDecoder
{
    /// <summary>Gets the Slice encoding decoded by this decoder.</summary>
    public SliceEncoding Encoding { get; }

    /// <summary>Gets the number of bytes decoded in the underlying buffer.</summary>
    public readonly long Consumed => _reader.Consumed;

    private const string EndOfBufferMessage = "Attempting to decode past the end of the Slice decoder buffer.";

    private static readonly IActivator _defaultActivator =
        ActivatorFactory.Instance.Get(typeof(SliceDecoder).Assembly);

    private static readonly UTF8Encoding _utf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true); // no BOM

    private readonly IActivator? _activator;

    private ClassContext _classContext;

    // The number of bytes already allocated for strings, dictionaries and sequences.
    private int _currentCollectionAllocation;

    // The current depth when decoding a class recursively.
    private int _currentDepth;

    // The maximum number of bytes that can be allocated for strings, dictionaries and sequences.
    private readonly int _maxCollectionAllocation;

    // The maximum depth when decoding a class recursively.
    private readonly int _maxDepth;

    private readonly Func<ServiceAddress, GenericProxy?, GenericProxy>? _proxyFactory;

    // The sequence reader.
    private SequenceReader<byte> _reader;

    private readonly GenericProxy? _templateProxy;

    /// <summary>Constructs a new Slice decoder over a byte buffer.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="proxyFactory">The proxy factory.</param>
    /// <param name="templateProxy">The template proxy to give to <paramref name="proxyFactory" />.</param>
    /// <param name="maxCollectionAllocation">The maximum cumulative allocation in bytes when decoding strings,
    /// sequences, and dictionaries from this buffer.<c>-1</c> (the default) is equivalent to 8 times the buffer
    /// length.</param>
    /// <param name="activator">The activator for decoding Slice1-encoded classes and exceptions.</param>
    /// <param name="maxDepth">The maximum depth when decoding a class recursively. The default is <c>3</c>.</param>
    public SliceDecoder(
        ReadOnlySequence<byte> buffer,
        SliceEncoding encoding,
        Func<ServiceAddress, GenericProxy?, GenericProxy>? proxyFactory = null,
        GenericProxy? templateProxy = null,
        int maxCollectionAllocation = -1,
        IActivator? activator = null,
        int maxDepth = 3)
    {
        Encoding = encoding;

        _currentCollectionAllocation = 0;
        _proxyFactory = proxyFactory;
        _templateProxy = templateProxy;

        _maxCollectionAllocation = maxCollectionAllocation == -1 ? 8 * (int)buffer.Length :
            (maxCollectionAllocation >= 0 ? maxCollectionAllocation :
                throw new ArgumentException(
                    $"The {nameof(maxCollectionAllocation)} argument must be greater than or equal to -1.",
                    nameof(maxCollectionAllocation)));

        _activator = activator;
        _classContext = default;
        _currentDepth = 0;
        _maxDepth = maxDepth >= 1 ? maxDepth :
            throw new ArgumentException($"The {nameof(maxDepth)} argument must be greater than 0.", nameof(maxDepth));

        _reader = new SequenceReader<byte>(buffer);
    }

    /// <summary>Constructs a new Slice decoder over a byte buffer.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="proxyFactory">The proxy factory.</param>
    /// <param name="templateProxy">The template proxy to give to <paramref name="proxyFactory" />.</param>
    /// <param name="maxCollectionAllocation">The maximum cumulative allocation in bytes when decoding strings,
    /// sequences, and dictionaries from this buffer.<c>-1</c> (the default) is equivalent to 8 times the buffer
    /// length.</param>
    /// <param name="activator">The activator for decoding Slice1-encoded classes and exceptions.</param>
    /// <param name="maxDepth">The maximum depth when decoding a class recursively. The default is <c>3</c>.</param>
    public SliceDecoder(
        ReadOnlyMemory<byte> buffer,
        SliceEncoding encoding,
        Func<ServiceAddress, GenericProxy?, GenericProxy>? proxyFactory = null,
        GenericProxy? templateProxy = null,
        int maxCollectionAllocation = -1,
        IActivator? activator = null,
        int maxDepth = 3)
        : this(
            new ReadOnlySequence<byte>(buffer),
            encoding,
            proxyFactory,
            templateProxy,
            maxCollectionAllocation,
            activator,
            maxDepth)
    {
    }

    // Decode methods for basic types

    /// <summary>Decodes a slice bool into a bool.</summary>
    /// <returns>The bool decoded by this decoder.</returns>
    public bool DecodeBool() =>
        _reader.TryRead(out byte value) ? value != 0 : throw new InvalidDataException(EndOfBufferMessage);

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
    public int DecodeSize() =>
        TryDecodeSize(out int value) ? value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes a Slice string into a string.</summary>
    /// <returns>The string decoded by this decoder.</returns>
    public string DecodeString() => DecodeStringBody(DecodeSize());

    /// <summary>Decodes <paramref name="size" /> UTF-8 bytes into a string.</summary>
    /// <param name="size">The number of UTF-8 bytes to read and decode.</param>
    /// <returns>The string decoded by this decoder.</returns>
    public string DecodeStringBody(int size)
    {
        if (size == 0)
        {
            return "";
        }
        else
        {
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

            // We can only compute the new allocation _after_ decoding the string. For dictionaries and sequences,
            // we perform this check before the allocation.
            IncreaseCollectionAllocation(result.Length * Unsafe.SizeOf<char>());
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
        catch (OverflowException ex)
        {
            throw new InvalidDataException("The value is out of the varint32 accepted range.", ex);
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
        catch (OverflowException ex)
        {
            throw new InvalidDataException("The value is out of the varuint62 accepted range.", ex);
        }
    }

    /// <summary>Decodes a Slice varuint62 into a ulong.</summary>
    /// <returns>The ulong decoded by this decoder.</returns>
    public ulong DecodeVarUInt62() =>
        TryDecodeVarUInt62(out ulong value) ? value : throw new InvalidDataException(EndOfBufferMessage);

    // Decode methods for constructed types

    /// <summary>Decodes a nullable proxy struct (Slice1 only).</summary>
    /// <typeparam name="TProxy">The type of the proxy struct to decode.</typeparam>
    /// <returns>The decoded proxy, or <see langword="null" />.</returns>
    public TProxy? DecodeNullableProxy<TProxy>() where TProxy : struct, IProxy =>
        this.DecodeNullableServiceAddress() is ServiceAddress serviceAddress ?
            CreateProxy<TProxy>(serviceAddress) : null;

    /// <summary>Decodes a proxy struct.</summary>
    /// <typeparam name="TProxy">The type of the proxy struct to decode.</typeparam>
    /// <returns>The decoded proxy struct.</returns>
    public TProxy DecodeProxy<TProxy>() where TProxy : struct, IProxy =>
        Encoding == SliceEncoding.Slice1 ?
            DecodeNullableProxy<TProxy>() ??
                throw new InvalidDataException("Decoded null for a non-nullable proxy.") :
           CreateProxy<TProxy>(this.DecodeServiceAddress());

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

    /// <summary>Decodes a Slice2-encoded tagged parameter or field.</summary>
    /// <typeparam name="T">The type of the decoded value.</typeparam>
    /// <param name="tag">The tag.</param>
    /// <param name="decodeFunc">A decode function that decodes the value of this tagged parameter or field.
    /// </param>
    /// <param name="useTagEndMarker">When <see langword="true" />, we are decoding a field and a tag end marker marks
    /// the end of the tagged fields. When <see langword="false" />, we are decoding a parameter and the end of the
    /// buffer marks the end of the tagged parameters.</param>
    /// <returns>The decoded value of the tagged parameter or field, or <see langword="null" /> if not found.</returns>
    /// <remarks>We return a T? and not a T to avoid ambiguities in the generated code with nullable reference types
    /// such as string?.</remarks>
    public T? DecodeTagged<T>(int tag, DecodeFunc<T> decodeFunc, bool useTagEndMarker)
    {
        if (Encoding == SliceEncoding.Slice1)
        {
            throw new InvalidOperationException("Slice1 encoded tags must be decoded with tag formats.");
        }

        int requestedTag = tag;

        while (useTagEndMarker || !_reader.End)
        {
            long startPos = _reader.Consumed;
            tag = DecodeVarInt32();

            if (tag == requestedTag)
            {
                // Found requested tag, so skip size:
                SkipSize();
                return decodeFunc(ref this);
            }
            else if ((useTagEndMarker && tag == Slice2Definitions.TagEndMarker) || tag > requestedTag)
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

    /// <summary>Decodes a Slice1-encoded tagged parameter or field.</summary>
    /// <typeparam name="T">The type of the decoded value.</typeparam>
    /// <param name="tag">The tag.</param>
    /// <param name="tagFormat">The expected tag format of this tag when found in the underlying buffer.</param>
    /// <param name="decodeFunc">A decode function that decodes the value of this tag.</param>
    /// <param name="useTagEndMarker">When <see langword="true" />, we are decoding a field and a tag end marker marks
    /// the end of the tagged fields. When <see langword="false" />, we are decoding a parameter and the end of the
    /// buffer marks the end of the tagged parameters.</param>
    /// <returns>The decoded value of the tagged parameter or field, or <see langword="null" /> if not found.</returns>
    /// <remarks>We return a T? and not a T to avoid ambiguities in the generated code with nullable reference types
    /// such as string?.</remarks>
    public T? DecodeTagged<T>(int tag, TagFormat tagFormat, DecodeFunc<T> decodeFunc, bool useTagEndMarker)
    {
        if (Encoding != SliceEncoding.Slice1)
        {
            throw new InvalidOperationException("Tag formats can only be used with the Slice1 encoding.");
        }

        if (DecodeTagHeader(tag, tagFormat, useTagEndMarker))
        {
            if (tagFormat == TagFormat.VSize)
            {
                SkipSize();
            }
            else if (tagFormat == TagFormat.FSize)
            {
                Skip(4);
            }
            return decodeFunc(ref this);
        }
        else
        {
            return default!; // i.e. null
        }
    }

    /// <summary>Gets a bit sequence reader to read the underlying bit sequence later on.</summary>
    /// <param name="bitSequenceSize">The minimum number of bits in the sequence.</param>
    /// <returns>A bit sequence reader.</returns>
    public BitSequenceReader GetBitSequenceReader(int bitSequenceSize)
    {
        if (Encoding == SliceEncoding.Slice1)
        {
            throw new InvalidOperationException("Cannot create a bit sequence reader using the Slice1 encoding.");
        }

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

    /// <summary>Skips the remaining tagged fields or parameters.</summary>
    /// <param name="useTagEndMarker">Whether or not the tagged fields or parameters use a tag end marker.
    /// </param>
    public void SkipTagged(bool useTagEndMarker)
    {
        if (Encoding == SliceEncoding.Slice1)
        {
            if (!useTagEndMarker && _classContext.Current.InstanceType != InstanceType.None)
            {
                throw new ArgumentException(
                    $"The {nameof(useTagEndMarker)} argument must be true when decoding a class/exception fields.",
                    nameof(useTagEndMarker));
            }
            else if (useTagEndMarker && _classContext.Current.InstanceType == InstanceType.None)
            {
                throw new ArgumentException(
                    $"The {nameof(useTagEndMarker)} argument must be false when decoding parameters.",
                    nameof(useTagEndMarker));
            }

            while (true)
            {
                if (!useTagEndMarker && _reader.End)
                {
                    // When we don't use an end marker, the end of the buffer indicates the end of the tagged params
                    // or fields.
                    break;
                }

                int v = DecodeUInt8();
                if (useTagEndMarker && v == TagEndMarker)
                {
                    // When we use an end marker, the end marker (and only the end marker) indicates the end of the
                    // tagged params / fields.
                    break;
                }

                var format = (TagFormat)(v & 0x07); // Read first 3 bits.
                if ((v >> 3) == 30)
                {
                    SkipSize();
                }
                SkipTaggedValue(format);
            }
        }
        else if (useTagEndMarker)
        {
            while (true)
            {
                if (DecodeVarInt32() == Slice2Definitions.TagEndMarker)
                {
                    break; // while
                }

                // Skip tagged value
                Skip(DecodeSize());
            }
        }
        else
        {
            while (!_reader.End)
            {
                Skip(DecodeVarInt62Length(PeekByte()));
                Skip(DecodeSize());
            }
        }
    }

    /// <summary>Skip Slice size.</summary>
    public void SkipSize()
    {
        if (Encoding == SliceEncoding.Slice1)
        {
            byte b = DecodeUInt8();
            if (b == 255)
            {
                Skip(4);
            }
        }
        else
        {
            Skip(DecodeVarInt62Length(PeekByte()));
        }
    }

    // Applies to all var type: varint62, varuint62 etc.
    internal static int DecodeVarInt62Length(byte from) => 1 << (from & 0x03);

    /// <summary>Verifies the Slice decoder has reached the end of its underlying buffer.</summary>
    /// <param name="skipTaggedParams">When <see langword="true" />, first skips all remaining tagged parameters in the
    /// current buffer.</param>
    internal void CheckEndOfBuffer(bool skipTaggedParams)
    {
        if (skipTaggedParams)
        {
            SkipTagged(useTagEndMarker: false);
        }

        if (!_reader.End)
        {
            throw new InvalidDataException($"There are {_reader.Remaining} bytes remaining in the buffer.");
        }
    }

    /// <summary>Copy all bytes from the underlying reader into the destination buffer writer.</summary>
    /// <remarks>This method also moves the reader's Consumed property.</remarks>
    internal void CopyTo(IBufferWriter<byte> destination)
    {
        destination.Write(_reader.UnreadSequence);
        _reader.AdvanceToEnd();
    }

    /// <summary>Decodes non-empty field dictionary without making a copy of the field values.</summary>
    /// <returns>The fields dictionary. The field values reference memory in the underlying buffer. They are not
    /// copied.</returns>
    internal Dictionary<TKey, ReadOnlySequence<byte>> DecodeShallowFieldDictionary<TKey>(
        int count,
        DecodeFunc<TKey> decodeKeyFunc)
        where TKey : struct
    {
        Debug.Assert(count > 0);

        // We don't use the normal collection allocation check here because SizeOf<ReadOnlySequence<byte>> is quite
        // large (24).
        // For example, say we decode a fields dictionary with a single field with an empty value. It's encoded
        // using 1 byte (dictionary size) + 1 byte (key) + 1 byte (value size) = 3 bytes. The decoder's default max
        // allocation size is 3 * 8 = 24. If we simply call IncreaseCollectionAllocation(1 * (4 + 24)), we'll exceed
        // the default collection allocation limit. (sizeof TKey is currently 4 but could/should increase to 8).

        // Each field consumes at least 2 bytes: 1 for the key and one for the value size.
        if (count * 2 > _reader.Remaining)
        {
            throw new InvalidDataException("Too many fields.");
        }

        var fields = new Dictionary<TKey, ReadOnlySequence<byte>>(count);

        for (int i = 0; i < count; ++i)
        {
            TKey key = decodeKeyFunc(ref this);
            int valueSize = DecodeSize();
            if (valueSize > _reader.Remaining)
            {
                throw new InvalidDataException($"The value of field '{key}' extends beyond the end of the buffer.");
            }
            ReadOnlySequence<byte> value = _reader.UnreadSequence.Slice(0, valueSize);
            _reader.Advance(valueSize);
            fields.Add(key, value);
        }
        return fields;
    }

    internal void IncreaseCollectionAllocation(int byteCount)
    {
        _currentCollectionAllocation += byteCount;
        if (_currentCollectionAllocation > _maxCollectionAllocation)
        {
            throw new InvalidDataException(
                $"The decoding exceeds the max collection allocation of '{_maxCollectionAllocation}'.");
        }
    }

    internal string ReadBytesAsBase64String(int byteCount)
    {
        using IMemoryOwner<byte>? memoryOwner =
            _reader.UnreadSpan.Length < byteCount ? MemoryPool<byte>.Shared.Rent(byteCount) : null;

        ReadOnlySpan<byte> vSpan;

        if (memoryOwner?.Memory is Memory<byte> buffer)
        {
            Span<byte> span = buffer.Span[0..byteCount];
            CopyTo(span);
            vSpan = span;
        }
        else
        {
            if (_reader.UnreadSpan.Length >= byteCount)
            {
                vSpan = _reader.UnreadSpan[0..byteCount];
                _reader.Advance(byteCount);
            }
            else
            {
                throw new InvalidDataException(EndOfBufferMessage);
            }
        }
        return Convert.ToBase64String(vSpan);
    }

    /// <summary>Tries to decode a Slice uint8 into a byte.</summary>
    /// <param name="value">When this method returns <see langword="true" />, this value is set to the decoded byte.
    /// Otherwise, this value is set to its default value.</param>
    /// <returns><see langword="true" /> if the decoder is not at the end of the buffer and the decode operation
    /// succeeded; otherwise, <see langword="false" />.</returns>
    internal bool TryDecodeUInt8(out byte value) => _reader.TryRead(out value);

    /// <summary>Tries to decode a Slice int32 into an int.</summary>
    /// <param name="value">When this method returns <see langword="true" />, this value is set to the decoded int.
    /// Otherwise, this value is set to its default value.</param>
    /// <returns><see langword="true" /> if the decoder is not at the end of the buffer and the decode operation
    /// succeeded; otherwise, <see langword="false" />.</returns>
    internal bool TryDecodeInt32(out int value) => SequenceMarshal.TryRead(ref _reader, out value);

    /// <summary>Tries to decode a size encoded on a variable number of bytes.</summary>
    /// <param name="size">When this method returns <see langword="true" />, this value is set to the decoded size.
    /// Otherwise, this value is set to its default value.</param>
    /// <returns><see langword="true" /> if the decoder is not at the end of the buffer and the decode operation
    /// succeeded; otherwise, <see langword="false" />.</returns>
    internal bool TryDecodeSize(out int size)
    {
        if (Encoding == SliceEncoding.Slice1)
        {
            if (TryDecodeUInt8(out byte firstByte))
            {
                if (firstByte < 255)
                {
                    size = firstByte;
                    return true;
                }
                else if (TryDecodeInt32(out size))
                {
                    if (size < 0)
                    {
                        throw new InvalidDataException($"Decoded invalid size: {size}.");
                    }
                    return true;
                }
            }
            size = 0;
            return false;
        }
        else
        {
            if (TryDecodeVarUInt62(out ulong v))
            {
                try
                {
                    size = checked((int)v);
                    return true;
                }
                catch (OverflowException ex)
                {
                    throw new InvalidDataException("Cannot decode size larger than int.MaxValue.", ex);
                }
            }
            else
            {
                size = 0;
                return false;
            }
        }
    }

    /// <summary>Tries to decode a Slice varuint62 into a ulong.</summary>
    /// <param name="value">When this method returns <see langword="true" />, this value is set to the decoded ulong.
    /// Otherwise, this value is set to its default value.</param>
    /// <returns><see langword="true" /> if the decoder is not at the end of the buffer and the decode operation
    /// succeeded; otherwise, <see langword="false" />.</returns>
    internal bool TryDecodeVarUInt62(out ulong value)
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

    private TProxy CreateProxy<TProxy>(ServiceAddress serviceAddress) where TProxy : struct, IProxy
    {
        if (_proxyFactory is null)
        {
            return _templateProxy is GenericProxy templateProxy ?
                new TProxy
                {
                    EncodeOptions = templateProxy.EncodeOptions,
                    Invoker = templateProxy.Invoker,
                    ServiceAddress = serviceAddress.Protocol is null ?
                        templateProxy.ServiceAddress with { Path = serviceAddress.Path } : serviceAddress
                }
                :
                new TProxy { ServiceAddress = serviceAddress };
        }
        else
        {
            GenericProxy proxy = _proxyFactory(serviceAddress, _templateProxy);

            return new TProxy
            {
                EncodeOptions = proxy.EncodeOptions,
                Invoker = proxy.Invoker,
                ServiceAddress = proxy.ServiceAddress
            };
        }
    }

    private bool DecodeTagHeader(int tag, TagFormat expectedFormat, bool useTagEndMarker)
    {
        Debug.Assert(Encoding == SliceEncoding.Slice1);

        if (!useTagEndMarker && _classContext.Current.InstanceType != InstanceType.None)
        {
            throw new ArgumentException(
                $"The {nameof(useTagEndMarker)} argument must be true when decoding the fields of a class or exception.",
                nameof(useTagEndMarker));
        }
        else if (useTagEndMarker && _classContext.Current.InstanceType == InstanceType.None)
        {
            throw new ArgumentException(
                $"The {nameof(useTagEndMarker)} argument must be false when decoding parameters.",
                nameof(useTagEndMarker));
        }

        if (_classContext.Current.InstanceType != InstanceType.None)
        {
            // tagged fields of a class or exception
            if ((_classContext.Current.SliceFlags & SliceFlags.HasTaggedFields) == 0)
            {
                // The current slice has no tagged parameter.
                return false;
            }
        }

        int requestedTag = tag;

        while (true)
        {
            if (!useTagEndMarker && _reader.End)
            {
                return false; // End of buffer indicates end of tagged parameters.
            }

            long savedPos = _reader.Consumed;

            int v = DecodeUInt8();
            if (useTagEndMarker && v == TagEndMarker)
            {
                _reader.Rewind(_reader.Consumed - savedPos);
                return false;
            }

            var format = (TagFormat)(v & 0x07); // First 3 bits.
            tag = v >> 3;
            if (tag == 30)
            {
                tag = DecodeSize();
            }

            if (tag > requestedTag)
            {
                _reader.Rewind(_reader.Consumed - savedPos);
                return false; // No tagged parameter with the requested tag.
            }
            else if (tag < requestedTag)
            {
                SkipTaggedValue(format);
            }
            else
            {
                if (expectedFormat == TagFormat.OptimizedVSize)
                {
                    expectedFormat = TagFormat.VSize; // fix virtual tag format
                }

                if (format != expectedFormat)
                {
                    throw new InvalidDataException($"Invalid tagged parameter '{tag}': unexpected format.");
                }
                return true;
            }
        }
    }

    private readonly byte PeekByte() =>
        _reader.TryPeek(out byte value) ? value : throw new InvalidDataException(EndOfBufferMessage);

    private void SkipTaggedValue(TagFormat format)
    {
        Debug.Assert(Encoding == SliceEncoding.Slice1);

        switch (format)
        {
            case TagFormat.F1:
                Skip(1);
                break;
            case TagFormat.F2:
                Skip(2);
                break;
            case TagFormat.F4:
                Skip(4);
                break;
            case TagFormat.F8:
                Skip(8);
                break;
            case TagFormat.Size:
                SkipSize();
                break;
            case TagFormat.VSize:
                Skip(DecodeSize());
                break;
            case TagFormat.FSize:
                int size = DecodeInt32();
                if (size < 0)
                {
                    throw new InvalidDataException($"Decoded invalid size: {size}.");
                }
                Skip(size);
                break;
            default:
                throw new InvalidDataException($"Cannot skip tagged parameter or field with tag format '{format}'.");
        }
    }
}
