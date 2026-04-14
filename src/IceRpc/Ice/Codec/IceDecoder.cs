// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec.Internal;
using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using static IceRpc.Ice.Codec.Internal.IceEncodingDefinitions;

namespace IceRpc.Ice.Codec;

/// <summary>Provides methods to decode data encoded with Ice.</summary>
public ref partial struct IceDecoder
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

    private const string EndOfBufferMessage = "Attempting to decode past the end of the Ice decoder buffer.";

    private static readonly IActivator _defaultActivator =
        ActivatorFactory.Instance.Get(typeof(IceDecoder).Assembly);

    private static readonly UTF8Encoding _utf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true); // no BOM

    private readonly IActivator? _activator;

    private ClassContext _classContext;

    // The number of bytes already allocated for strings, dictionaries, and sequences.
    private int _currentCollectionAllocation;

    // The current depth when decoding a class recursively.
    private int _currentDepth;

    // The maximum number of bytes that can be allocated for strings, dictionaries, and sequences.
    private readonly int _maxCollectionAllocation;

    // The maximum depth when decoding a class recursively.
    private readonly int _maxDepth;

    // The sequence reader.
    private SequenceReader<byte> _reader;

    /// <summary>Constructs a new Ice decoder over a byte buffer.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="decodingContext">The decoding context.</param>
    /// <param name="maxCollectionAllocation">The maximum cumulative allocation in bytes when decoding strings,
    /// sequences, and dictionaries from this buffer.<c>-1</c> (the default) is equivalent to 8 times the buffer
    /// length.</param>
    /// <param name="activator">The activator for decoding classes and exceptions.</param>
    /// <param name="maxDepth">The maximum depth when decoding a class recursively. The default is <c>3</c>.</param>
    public IceDecoder(
        ReadOnlySequence<byte> buffer,
        object? decodingContext = null,
        int maxCollectionAllocation = -1,
        IActivator? activator = null,
        int maxDepth = 3)
    {
        DecodingContext = decodingContext;

        _currentCollectionAllocation = 0;

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

    /// <summary>Constructs a new Ice decoder over a byte buffer.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="decodingContext">The decoding context.</param>
    /// <param name="maxCollectionAllocation">The maximum cumulative allocation in bytes when decoding strings,
    /// sequences, and dictionaries from this buffer.<c>-1</c> (the default) is equivalent to 8 times the buffer
    /// length.</param>
    /// <param name="activator">The activator for decoding classes and exceptions.</param>
    /// <param name="maxDepth">The maximum depth when decoding a class recursively. The default is <c>3</c>.</param>
    public IceDecoder(
        ReadOnlyMemory<byte> buffer,
        object? decodingContext = null,
        int maxCollectionAllocation = -1,
        IActivator? activator = null,
        int maxDepth = 3)
        : this(
            new ReadOnlySequence<byte>(buffer),
            decodingContext,
            maxCollectionAllocation,
            activator,
            maxDepth)
    {
    }

    // Decode methods for basic types

    /// <summary>Checks if the in memory representation of the bool value is valid according to the Ice encoding.</summary>
    /// <param name="value">The value to check.</param>
    /// <exception cref="InvalidDataException">If the value is out of the bool type accepted range.</exception>
    public static void CheckBoolValue(bool value)
    {
        if (Unsafe.As<bool, byte>(ref value) > 1)
        {
            throw new InvalidDataException("The value is out of the bool type accepted range.");
        }
    }

    /// <summary>Decodes an Ice bool into a bool.</summary>
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

    /// <summary>Decodes an Ice byte into a byte.</summary>
    /// <returns>The byte decoded by this decoder.</returns>
    public byte DecodeByte() =>
        _reader.TryRead(out byte value) ? value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes an Ice double into a double.</summary>
    /// <returns>The double decoded by this decoder.</returns>
    public double DecodeDouble() =>
        SequenceMarshal.TryRead(ref _reader, out double value) ?
            value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes an Ice float into a float.</summary>
    /// <returns>The float decoded by this decoder.</returns>
    public float DecodeFloat() =>
        SequenceMarshal.TryRead(ref _reader, out float value) ?
            value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes an Ice int into an int.</summary>
    /// <returns>The int decoded by this decoder.</returns>
    public int DecodeInt() =>
        SequenceMarshal.TryRead(ref _reader, out int value) ?
            value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes an Ice long into a long.</summary>
    /// <returns>The long decoded by this decoder.</returns>
    public long DecodeLong() =>
        SequenceMarshal.TryRead(ref _reader, out long value) ?
            value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes an Ice short into a short.</summary>
    /// <returns>The short decoded by this decoder.</returns>
    public short DecodeShort() =>
        SequenceMarshal.TryRead(ref _reader, out short value) ?
            value : throw new InvalidDataException(EndOfBufferMessage);

    /// <summary>Decodes a size encoded on a variable number of bytes.</summary>
    /// <returns>The size decoded by this decoder.</returns>
    public int DecodeSize()
    {
        byte firstByte = DecodeByte();
        if (firstByte < 255)
        {
            return firstByte;
        }
        else
        {
            int size = DecodeInt();
            if (size < 0)
            {
                throw new InvalidDataException($"Decoded invalid size: {size}.");
            }
            return size;
        }
    }

    /// <summary>Decodes an Ice string into a string.</summary>
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
            IncreaseCollectionAllocation((long)result.Length * Unsafe.SizeOf<char>());
            return result;
        }
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

    /// <summary>Decodes a tagged field.</summary>
    /// <typeparam name="T">The type of the decoded value.</typeparam>
    /// <param name="tag">The tag.</param>
    /// <param name="tagFormat">The expected tag format of this tag when found in the underlying buffer.</param>
    /// <param name="decodeFunc">A decode function that decodes the value of this tag.</param>
    /// <returns>The decoded value of the tagged field, or <see langword="null" /> if not found.</returns>
    /// <remarks>We return a T? and not a T to avoid ambiguities in the generated code with nullable reference types
    /// such as string?.</remarks>
    public T? DecodeTagged<T>(int tag, TagFormat tagFormat, DecodeFunc<T> decodeFunc)
    {
        if (DecodeTagHeader(tag, tagFormat))
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

    /// <summary>Increases the number of bytes in the decoder's collection allocation.</summary>
    /// <param name="byteCount">The number of bytes to add. Must be greater than or equal to 0.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="byteCount" /> is negative.</exception>
    /// <exception cref="InvalidDataException">Thrown when the total number of bytes exceeds the max collection
    /// allocation.</exception>
    /// <seealso cref="IceDecoder(ReadOnlySequence{byte}, object?, int, IActivator?, int)" />
    public void IncreaseCollectionAllocation(long byteCount)
    {
        if (byteCount < 0)
        {
            throw new ArgumentException(
                $"The {nameof(byteCount)} argument must be greater than or equal to 0.",
                nameof(byteCount));
        }
        long newAllocation = _currentCollectionAllocation + byteCount;
        if (newAllocation > _maxCollectionAllocation)
        {
            throw new InvalidDataException(
                $"The decoding exceeds the max collection allocation of '{_maxCollectionAllocation}'.");
        }
        _currentCollectionAllocation = (int)newAllocation;
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
        // True when decoding a class or exception, false when decoding parameters. Keep in mind we never decode a
        // class while decoding a tagged parameter.
        bool useTagEndMarker = _classContext.Current.InstanceType != InstanceType.None;

        while (true)
        {
            if (!useTagEndMarker && _reader.End)
            {
                // When we don't use an end marker, the end of the buffer indicates the end of the tagged fields.
                break;
            }

            int v = DecodeByte();
            if (useTagEndMarker && v == TagEndMarker)
            {
                // When we use an end marker, the end marker (and only the end marker) indicates the end of the
                // tagged fields.
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

    /// <summary>Skip Ice size.</summary>
    public void SkipSize()
    {
        byte b = DecodeByte();
        if (b == 255)
        {
            Skip(4);
        }
    }

    private bool DecodeTagHeader(int tag, TagFormat expectedFormat)
    {
        // True when decoding a class or exception, false when decoding parameters. Keep in mind we never decode a
        // class while decoding a tagged parameter.
        bool useTagEndMarker = _classContext.Current.InstanceType != InstanceType.None;

        if (_classContext.Current.InstanceType != InstanceType.None)
        {
            // tagged fields of a class or exception
            if ((_classContext.Current.SliceFlags & SliceFlags.HasTaggedFields) == 0)
            {
                // The current slice has no tagged field.
                return false;
            }
        }

        int requestedTag = tag;

        while (true)
        {
            if (!useTagEndMarker && _reader.End)
            {
                return false; // End of buffer indicates end of tagged fields.
            }

            long savedPos = _reader.Consumed;

            int v = DecodeByte();
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
                return false; // No tagged field with the requested tag.
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
                    throw new InvalidDataException($"Invalid tagged field '{tag}': unexpected format.");
                }
                return true;
            }
        }
    }

    private void SkipTaggedValue(TagFormat format)
    {
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
                int size = DecodeInt();
                if (size < 0)
                {
                    throw new InvalidDataException($"Decoded invalid size: {size}.");
                }
                Skip(size);
                break;
            default:
                throw new InvalidDataException($"Cannot skip tagged field with tag format '{format}'.");
        }
    }
}
