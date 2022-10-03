// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using static IceRpc.Slice.Internal.Slice1Definitions;

namespace IceRpc.Slice;

/// <summary>Decodes a byte buffer encoded using the Slice encoding.</summary>
public ref partial struct SliceDecoder
{
    /// <summary>Gets the Slice encoding decoded by this decoder.</summary>
    public SliceEncoding Encoding { get; }

    /// <summary>Gets the number of bytes decoded in the underlying buffer.</summary>
    public long Consumed => _reader.Consumed;

    private static readonly IActivator _defaultActivator =
        ActivatorFactory.Instance.Get(typeof(SliceDecoder).Assembly);

    private static readonly UTF8Encoding _utf8 =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true); // no BOM

    /// <summary>Gets or creates an activator for the Slice types in the specified assembly and its referenced
    /// assemblies.</summary>
    /// <param name="assembly">The assembly.</param>
    /// <returns>An activator that activates the Slice types defined in <paramref name="assembly"/> provided this
    /// assembly contains generated code (as determined by the presence of the <see cref="SliceAttribute"/>
    /// attribute). Types defined in assemblies referenced by <paramref name="assembly"/> are included as well,
    /// recursively. The types defined in the referenced assemblies of an assembly with no generated code are not
    /// considered.</returns>
    public static IActivator GetActivator(Assembly assembly) => ActivatorFactory.Instance.Get(assembly);

    /// <summary>Gets or creates an activator for the Slice types defined in the specified assemblies and their
    /// referenced assemblies.</summary>
    /// <param name="assemblies">The assemblies.</param>
    /// <returns>An activator that activates the Slice types defined in <paramref name="assemblies"/> and their
    /// referenced assemblies. See <see cref="GetActivator(Assembly)"/>.</returns>
    public static IActivator GetActivator(IEnumerable<Assembly> assemblies) =>
        Internal.Activator.Merge(assemblies.Select(assembly => ActivatorFactory.Instance.Get(assembly)));

    private readonly IActivator _activator;

    private ClassContext _classContext;

    // The number of bytes already allocated for strings, dictionaries and sequences.
    private int _currentCollectionAllocation;

    // The current depth when decoding a type recursively.
    private int _currentDepth;

    // The maximum number of bytes that can be allocated for strings, dictionaries and sequences.
    private readonly int _maxCollectionAllocation;

    // The maximum depth when decoding a type recursively.
    private readonly int _maxDepth;

    // The sequence reader.
    private SequenceReader<byte> _reader;

    private readonly Func<ServiceAddress, ServiceProxy?, ServiceProxy>? _serviceProxyFactory;
    private readonly ServiceProxy? _templateProxy;

    /// <summary>Constructs a new Slice decoder over a byte buffer.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="activator">The activator.</param>
    /// <param name="serviceProxyFactory">The service proxy factory.</param>
    /// <param name="templateProxy">The template proxy to give to <paramref name="serviceProxyFactory"/>.</param>
    /// <param name="maxCollectionAllocation">The maximum cumulative allocation in bytes when decoding strings,
    /// sequences, and dictionaries from this buffer.<c>-1</c> (the default) is equivalent to 8 times the buffer
    /// length.</param>
    /// <param name="maxDepth">The maximum depth when decoding a type recursively. The default is <c>3</c>.</param>
    public SliceDecoder(
        ReadOnlySequence<byte> buffer,
        SliceEncoding encoding,
        IActivator? activator = null,
        Func<ServiceAddress, ServiceProxy?, ServiceProxy>? serviceProxyFactory = null,
        ServiceProxy? templateProxy = null,
        int maxCollectionAllocation = -1,
        int maxDepth = 3)
    {
        Encoding = encoding;

        _activator = activator ?? _defaultActivator;
        _classContext = default;

        _currentCollectionAllocation = 0;
        _currentDepth = 0;

        _serviceProxyFactory = serviceProxyFactory;
        _templateProxy = templateProxy;

        _maxCollectionAllocation = maxCollectionAllocation == -1 ? 8 * (int)buffer.Length :
            (maxCollectionAllocation >= 0 ? maxCollectionAllocation :
                throw new ArgumentException(
                    $"{nameof(maxCollectionAllocation)} must be greater than or equal to -1",
                    nameof(maxCollectionAllocation)));

        _maxDepth = maxDepth >= 1 ? maxDepth :
            throw new ArgumentException($"{nameof(maxDepth)} must be greater than 0", nameof(maxDepth));

        _reader = new SequenceReader<byte>(buffer);
    }

    /// <summary>Constructs a new Slice decoder over a byte buffer.</summary>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="encoding">The Slice encoding version.</param>
    /// <param name="activator">The activator.</param>
    /// <param name="serviceProxyFactory">The service proxy factory.</param>
    /// <param name="templateProxy">The template proxy to give to <paramref name="serviceProxyFactory"/>.</param>
    /// <param name="maxCollectionAllocation">The maximum cumulative allocation in bytes when decoding strings,
    /// sequences, and dictionaries from this buffer.<c>-1</c> (the default) is equivalent to 8 times the buffer
    /// length.</param>
    /// <param name="maxDepth">The maximum depth when decoding a type recursively. The default is <c>3</c>.</param>
    public SliceDecoder(
        ReadOnlyMemory<byte> buffer,
        SliceEncoding encoding,
        IActivator? activator = null,
        Func<ServiceAddress, ServiceProxy?, ServiceProxy>? serviceProxyFactory = null,
        ServiceProxy? templateProxy = null,
        int maxCollectionAllocation = -1,
        int maxDepth = 3)
        : this(
            new ReadOnlySequence<byte>(buffer),
            encoding,
            activator,
            serviceProxyFactory,
            templateProxy,
            maxCollectionAllocation,
            maxDepth)
    {
    }

    // Decode methods for basic types

    /// <summary>Decodes a slice bool into a bool.</summary>
    /// <returns>The bool decoded by this decoder.</returns>
    public bool DecodeBool() => _reader.TryRead(out byte value) ? value != 0 : throw new EndOfBufferException();

    /// <summary>Decodes a Slice float32 into a float.</summary>
    /// <returns>The float decoded by this decoder.</returns>
    public float DecodeFloat32() =>
        SequenceMarshal.TryRead(ref _reader, out float value) ? value : throw new EndOfBufferException();

    /// <summary>Decodes a Slice float64 into a double.</summary>
    /// <returns>The double decoded by this decoder.</returns>
    public double DecodeFloat64() =>
        SequenceMarshal.TryRead(ref _reader, out double value) ? value : throw new EndOfBufferException();

    /// <summary>Decodes a Slice int8 into an sbyte.</summary>
    /// <returns>The sbyte decoded by this decoder.</returns>
    public sbyte DecodeInt8() => (sbyte)DecodeUInt8();

    /// <summary>Decodes a Slice int16 into a short.</summary>
    /// <returns>The short decoded by this decoder.</returns>
    public short DecodeInt16() =>
        SequenceMarshal.TryRead(ref _reader, out short value) ? value : throw new EndOfBufferException();

    /// <summary>Decodes a Slice int32 into an int.</summary>
    /// <returns>The int decoded by this decoder.</returns>
    public int DecodeInt32() =>
        SequenceMarshal.TryRead(ref _reader, out int value) ? value : throw new EndOfBufferException();

    /// <summary>Decodes a Slice int64 into a long.</summary>
    /// <returns>The long decoded by this decoder.</returns>
    public long DecodeInt64() =>
        SequenceMarshal.TryRead(ref _reader, out long value) ? value : throw new EndOfBufferException();

    /// <summary>Decodes a size encoded on a variable number of bytes.</summary>
    /// <returns>The size decoded by this decoder.</returns>
    public int DecodeSize() => TryDecodeSize(out int value) ? value : throw new EndOfBufferException();

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
            string result;
            if (_reader.UnreadSpan.Length >= size)
            {
                try
                {
                    result = _utf8.GetString(_reader.UnreadSpan[0..size]);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
                {
                    // The two exceptions that can be thrown by GetString are ArgumentException and
                    // DecoderFallbackException. Both of which are a result of malformed data. As such, we can just
                    // throw an InvalidDataException.
                    throw new InvalidDataException("invalid UTF-8 string", ex);
                }
            }
            else
            {
                ReadOnlySequence<byte> bytes = _reader.UnreadSequence;
                if (size > bytes.Length)
                {
                    throw new EndOfBufferException();
                }
                try
                {
                    result = _utf8.GetString(bytes.Slice(0, size));
                }
                catch (Exception ex) when (ex is ArgumentException || ex is DecoderFallbackException)
                {
                    // The two exceptions that can be thrown by GetString are ArgumentException and
                    // DecoderFallbackException. Both of which are a result of malformed data. As such, we can just
                    // throw an InvalidDataException.
                    throw new InvalidDataException("invalid UTF-8 string", ex);
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
    public byte DecodeUInt8() => _reader.TryRead(out byte value) ? value : throw new EndOfBufferException();

    /// <summary>Decodes a Slice uint16 into a ushort.</summary>
    /// <returns>The ushort decoded by this decoder.</returns>
    public ushort DecodeUInt16() =>
        SequenceMarshal.TryRead(ref _reader, out ushort value) ? value : throw new EndOfBufferException();

    /// <summary>Decodes a Slice uint32 into a uint.</summary>
    /// <returns>The uint decoded by this decoder.</returns>
    public uint DecodeUInt32() =>
        SequenceMarshal.TryRead(ref _reader, out uint value) ? value : throw new EndOfBufferException();

    /// <summary>Decodes a Slice uint64 into a ulong.</summary>
    /// <returns>The ulong decoded by this decoder.</returns>
    public ulong DecodeUInt64() =>
        SequenceMarshal.TryRead(ref _reader, out ulong value) ? value : throw new EndOfBufferException();

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
            throw new InvalidDataException("varint32 value is out of range", ex);
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
            throw new InvalidDataException("varuint62 value is out of range", ex);
        }
    }

    /// <summary>Decodes a Slice varuint62 into a ulong.</summary>
    /// <returns>The ulong decoded by this decoder.</returns>
    public ulong DecodeVarUInt62() =>
        TryDecodeVarUInt62(out ulong value) ? value : throw new EndOfBufferException();

    // Decode methods for constructed types

    /// <summary>Decodes a trait.</summary>
    /// <typeparam name="T">The type of the decoded trait.</typeparam>
    /// <param name="fallback">An optional function that creates a trait in case the activator does not find a
    /// struct or class associated with the type ID.</param>
    /// <returns>The decoded trait.</returns>
    public T DecodeTrait<T>(DecodeTraitFunc<T>? fallback = null)
    {
        if (Encoding == SliceEncoding.Slice1)
        {
            throw new InvalidOperationException(
                $"{nameof(DecodeTrait)} is not compatible with encoding {Encoding}");
        }

        string typeId = DecodeString();

        if (++_currentDepth > _maxDepth)
        {
            throw new InvalidDataException($"maximum decoder depth reached while decoding trait {typeId}");
        }

        object? instance = _activator.CreateInstance(typeId, ref this);
        _currentDepth--;

        if (instance is null)
        {
            return fallback is not null ? fallback(typeId, ref this) :
                throw new InvalidDataException($"activator could not find type with Slice type ID '{typeId}'");
        }
        else
        {
            return instance is T result ? result : throw new InvalidDataException(
                $"decoded instance of type '{instance.GetType()}' does not implement '{typeof(T)}'");
        }
    }

    /// <summary>Decodes a nullable proxy struct (Slice1 only).</summary>
    /// <typeparam name="TProxy">The type of the proxy struct to decode.</typeparam>
    /// <returns>The decoded Proxy, or null.</returns>
    public TProxy? DecodeNullableProxy<TProxy>() where TProxy : struct, IProxy
    {
        if (Encoding != SliceEncoding.Slice1)
        {
            throw new InvalidOperationException($"decoding a nullable Proxy with {Encoding} requires a bit sequence");
        }
        string path = this.DecodeIdentityPath();
        return path != "/" ? CreateProxy<TProxy>(DecodeServiceAddress(path)) : null;
    }

    /// <summary>Decodes a proxy struct.</summary>
    /// <typeparam name="TProxy">The type of the proxy struct to decode.</typeparam>
    /// <returns>The decoded proxy struct.</returns>
    public TProxy DecodeProxy<TProxy>() where TProxy : struct, IProxy
    {
        if (Encoding == SliceEncoding.Slice1)
        {
            string path = this.DecodeIdentityPath();
            return path != "/" ? CreateProxy<TProxy>(DecodeServiceAddress(path)) :
                throw new InvalidDataException("decoded null for a non-nullable proxy");
        }
        else
        {
            string serviceAddressString = DecodeString();
            ServiceAddress serviceAddress;
            try
            {
                if (serviceAddressString.StartsWith('/'))
                {
                    // relative service address
                    serviceAddress = new ServiceAddress { Path = serviceAddressString };
                }
                else
                {
                    serviceAddress = new ServiceAddress(new Uri(serviceAddressString, UriKind.Absolute));
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("received invalid service address", ex);
            }

            return CreateProxy<TProxy>(serviceAddress);
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
            throw new EndOfBufferException();
        }
    }

    /// <summary>Decodes a Slice1 system exception.</summary>
    /// <returns>The decoded exception.</returns>
    public DispatchException DecodeSystemException()
    {
        if (Encoding != SliceEncoding.Slice1)
        {
            throw new InvalidOperationException($"{nameof(DecodeSystemException)} is not compatible with {Encoding}");
        }

        ReplyStatus replyStatus = this.DecodeReplyStatus();

        if (replyStatus <= ReplyStatus.UserException)
        {
            throw new InvalidDataException($"invalid system exception with {replyStatus} ReplyStatus");
        }

        string? message = null;
        DispatchErrorCode errorCode;

        switch (replyStatus)
        {
            case ReplyStatus.FacetNotExistException:
            case ReplyStatus.ObjectNotExistException:
            case ReplyStatus.OperationNotExistException:

                var requestFailed = new RequestFailedExceptionData(ref this);

                errorCode = replyStatus == ReplyStatus.OperationNotExistException ?
                    DispatchErrorCode.OperationNotFound : DispatchErrorCode.ServiceNotFound;

                if (requestFailed.Operation.Length > 0)
                {
                    string target = requestFailed.Fragment.Length > 0 ?
                        $"{requestFailed.Path}#{requestFailed.Fragment}" : requestFailed.Path;

                    message = $"{nameof(DispatchException)} {{ ErrorCode = {errorCode} }} while dispatching '{requestFailed.Operation}' on '{target}'";
                }
                // else message remains null
                break;

            default:
                message = DecodeString();
                errorCode = DispatchErrorCode.UnhandledException;

                // Attempt to parse the DispatchErrorCode from the message:
                if (message.StartsWith('[') &&
                    message.IndexOf(']', StringComparison.Ordinal) is int pos && pos != -1)
                {
                    try
                    {
                        errorCode = (DispatchErrorCode)byte.Parse(
                            message[1..pos],
                            CultureInfo.InvariantCulture);

                        message = message[(pos + 1)..].TrimStart();
                    }
                    catch
                    {
                        // ignored, keep default errorCode
                    }
                }
                break;
        }

        return new DispatchException(message, errorCode)
        {
            ConvertToUnhandled = true,
        };
    }

    /// <summary>Decodes a Slice2-encoded tagged parameter or data member.</summary>
    /// <typeparam name="T">The type of the decoded value.</typeparam>
    /// <param name="tag">The tag.</param>
    /// <param name="decodeFunc">A decode function that decodes the value of this tagged parameter or data member.
    /// </param>
    /// <param name="useTagEndMarker">When <see langword="true" />, we are decoding a data member and a tag end marker
    /// marks the end of the tagged data members. When <see langword="false" />, we are decoding a parameter and the end
    /// of the buffer marks the end of the tagged parameters.</param>
    /// <returns>The decoded value of the tagged parameter or data member, or null if not found.</returns>
    /// <remarks>We return a T? and not a T to avoid ambiguities in the generated code with nullable reference
    /// types such as string?.</remarks>
    public T? DecodeTagged<T>(int tag, DecodeFunc<T> decodeFunc, bool useTagEndMarker)
    {
        if (Encoding == SliceEncoding.Slice1)
        {
            throw new InvalidOperationException("Slice1 encoded tags must be decoded with tag formats");
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

    /// <summary>Decodes a Slice1-encoded tagged parameter or data member.</summary>
    /// <typeparam name="T">The type of the decoded value.</typeparam>
    /// <param name="tag">The tag.</param>
    /// <param name="tagFormat">The expected tag format of this tag when found in the underlying buffer.</param>
    /// <param name="decodeFunc">A decode function that decodes the value of this tag.</param>
    /// <param name="useTagEndMarker">When <see langword="true" />, we are decoding a data member and a tag end marker
    /// marks the end of the tagged data members. When <see langword="false" />, we are decoding a parameter and the end
    /// of the buffer marks the end of the tagged parameters.</param>
    /// <returns>The decoded value of the tagged parameter or data member, or null if not found.</returns>
    /// <remarks>We return a T? and not a T to avoid ambiguities in the generated code with nullable reference
    /// types such as string?.</remarks>
    public T? DecodeTagged<T>(int tag, TagFormat tagFormat, DecodeFunc<T> decodeFunc, bool useTagEndMarker)
    {
        if (Encoding != SliceEncoding.Slice1)
        {
            throw new InvalidOperationException("tag formats can only be used with the Slice1 encoding");
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
            throw new InvalidOperationException("cannot create a bit sequence reader with Slice1");
        }

        if (bitSequenceSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(bitSequenceSize),
                "bitSequenceSize must be greater than 0");
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
            throw new EndOfBufferException();
        }
    }

    /// <summary>Skips the remaining tagged data members or parameters.</summary>
    /// <param name="useTagEndMarker">Whether or not the tagged data members or parameters use a tag end marker.
    /// </param>
    public void SkipTagged(bool useTagEndMarker)
    {
        if (Encoding == SliceEncoding.Slice1)
        {
            if (!useTagEndMarker && _classContext.Current.InstanceType != InstanceType.None)
            {
                throw new ArgumentException(
                    $"{nameof(useTagEndMarker)} must be true when decoding a class/exception data members",
                    nameof(useTagEndMarker));
            }
            else if (useTagEndMarker && _classContext.Current.InstanceType == InstanceType.None)
            {
                throw new ArgumentException(
                    $"{nameof(useTagEndMarker)} must be false when decoding parameters",
                    nameof(useTagEndMarker));
            }

            while (true)
            {
                if (!useTagEndMarker && _reader.End)
                {
                    // When we don't use an end marker, the end of the buffer indicates the end of the tagged params
                    // or members.
                    break;
                }

                int v = DecodeUInt8();
                if (useTagEndMarker && v == TagEndMarker)
                {
                    // When we use an end marker, the end marker (and only the end marker) indicates the end of the
                    // tagged params / member.
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
            throw new InvalidDataException($"{_reader.Remaining} bytes remaining in the buffer");
        }
    }

    /// <summary>Copy all bytes from the underlying reader into the destination buffer writer.</summary>
    /// <remarks>This method also moves the reader's Consumed property.</remarks>
    internal void CopyTo(IBufferWriter<byte> destination)
    {
        destination.Write(_reader.UnreadSequence);
        _reader.AdvanceToEnd();
    }

    internal void IncreaseCollectionAllocation(int byteCount)
    {
        _currentCollectionAllocation += byteCount;
        if (_currentCollectionAllocation > _maxCollectionAllocation)
        {
            throw new InvalidDataException(
                $"decoding exceeds max collection allocation of '{_maxCollectionAllocation}'");
        }
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
            throw new InvalidDataException("too many fields");
        }

        var fields = new Dictionary<TKey, ReadOnlySequence<byte>>(count);

        for (int i = 0; i < count; ++i)
        {
            TKey key = decodeKeyFunc(ref this);
            int valueSize = DecodeSize();
            if (valueSize > _reader.Remaining)
            {
                throw new InvalidDataException($"the value of field '{key}' extends beyond the end of the buffer");
            }
            ReadOnlySequence<byte> value = _reader.UnreadSequence.Slice(0, valueSize);
            _reader.Advance(valueSize);
            fields.Add(key, value);
        }
        return fields;
    }

    /// <summary>Tries to decode a Slice uint8 into a byte.</summary>
    /// <param name="value">When this method returns <see langword="true" />, this value is set to the decoded byte.
    /// Otherwise, this value is set to its default value.</param>
    /// <returns><see langword="true" /> if the decoder is not at the end of the buffer and the decode operation
    /// succeeded; <see langword="false" /> otherwise.</returns>
    internal bool TryDecodeUInt8(out byte value) => _reader.TryRead(out value);

    /// <summary>Tries to decode a Slice int32 into an int.</summary>
    /// <param name="value">When this method returns <see langword="true" />, this value is set to the decoded int.
    /// Otherwise, this value is set to its default value.</param>
    /// <returns><see langword="true" /> if the decoder is not at the end of the buffer and the decode operation
    /// succeeded; <see langword="false" /> otherwise.</returns>
    internal bool TryDecodeInt32(out int value) => SequenceMarshal.TryRead(ref _reader, out value);

    /// <summary>Tries to decode a size encoded on a variable number of bytes.</summary>
    /// <param name="size">When this method returns <see langword="true" />, this value is set to the decoded size.
    /// Otherwise, this value is set to its default value.</param>
    /// <returns><see langword="true" /> if the decoder is not at the end of the buffer and the decode operation
    /// succeeded; <see langword="false" /> otherwise.</returns>
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
                        throw new InvalidDataException($"decoded invalid size: {size}");
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
                    throw new InvalidDataException("cannot decode size larger than int.MaxValue", ex);
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
    /// succeeded; <see langword="false" /> otherwise.</returns>
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
        if (_serviceProxyFactory is null)
        {
            return _templateProxy is ServiceProxy templateProxy ?
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
            ServiceProxy serviceProxy = _serviceProxyFactory(serviceAddress, _templateProxy);

            return new TProxy
            {
                EncodeOptions = serviceProxy.EncodeOptions,
                Invoker = serviceProxy.Invoker,
                ServiceAddress = serviceProxy.ServiceAddress
            };
        }
    }

    /// <summary>Decodes a server address (Slice1).</summary>
    /// <param name="protocol">The protocol of this server address.</param>
    /// <returns>The server address decoded by this decoder.</returns>
    private ServerAddress DecodeServerAddress(Protocol protocol)
    {
        Debug.Assert(Encoding == SliceEncoding.Slice1);

        // The Slice1 ice server addresses are transport-specific, and hard-coded here and in the
        // SliceEncoder. The preferred and fallback encoding for new transports is TransportCode.Uri.

        ServerAddress? serverAddress = null;
        TransportCode transportCode = this.DecodeTransportCode();

        int size = DecodeInt32();
        if (size < 6)
        {
            throw new InvalidDataException($"the Slice1 encapsulation's size ({size}) is too small");
        }

        if (size - 4 > _reader.Remaining)
        {
            throw new InvalidDataException(
                $"the encapsulation's size ({size}) extends beyond the end of the buffer");
        }

        // Remove 6 bytes from the encapsulation size (4 for encapsulation size, 2 for encoding).
        size -= 6;

        byte encodingMajor = DecodeUInt8();
        byte encodingMinor = DecodeUInt8();

        if (encodingMajor == 1 && encodingMinor <= 1)
        {
            long oldPos = _reader.Consumed;

            if (protocol == Protocol.Ice)
            {
                switch (transportCode)
                {
                    case TransportCode.Tcp:
                    case TransportCode.Ssl:
                    {
                        serverAddress = Transports.TcpClientTransport.DecodeServerAddress(
                            ref this,
                            transportCode == TransportCode.Tcp ? TransportNames.Tcp : TransportNames.Ssl);
                        break;
                    }

                    case TransportCode.Uri:
                        serverAddress = new ServerAddress(new Uri(DecodeString()));
                        if (serverAddress.Value.Protocol != protocol)
                        {
                            throw new InvalidDataException(
                                $"expected server address for {protocol} but received '{serverAddress.Value}'");
                        }
                        break;

                    default:
                    {
                        // Create a server address for transport opaque

                        using IMemoryOwner<byte>? memoryOwner =
                            _reader.UnreadSpan.Length < size ? MemoryPool<byte>.Shared.Rent(size) : null;

                        ReadOnlySpan<byte> vSpan;

                        if (memoryOwner?.Memory is Memory<byte> buffer)
                        {
                            Span<byte> span = buffer.Span[0..size];
                            CopyTo(span);
                            vSpan = span;
                        }
                        else
                        {
                            vSpan = _reader.UnreadSpan[0..size];
                            _reader.Advance(size);
                        }

                        var builder = ImmutableDictionary.CreateBuilder<string, string>();
                        builder.Add("t", ((short)transportCode).ToString(CultureInfo.InvariantCulture));
                        builder.Add("v", Convert.ToBase64String(vSpan));

                        serverAddress = new ServerAddress(
                            Protocol.Ice,
                            host: "opaque", // not a real host obviously
                            port: Protocol.Ice.DefaultPort,
                            TransportNames.Opaque,
                            builder.ToImmutable());
                        break;
                    }
                }
            }
            else if (transportCode == TransportCode.Uri)
            {
                // The server addresses of Slice1 encoded icerpc proxies only use TransportCode.Uri.
                serverAddress = new ServerAddress(new Uri(DecodeString()));
                if (serverAddress.Value.Protocol != protocol)
                {
                    throw new InvalidDataException(
                        $"expected {protocol} server address but received '{serverAddress.Value}'");
                }
            }

            if (serverAddress is not null)
            {
                // Make sure we read the full encapsulation.
                if (_reader.Consumed != oldPos + size)
                {
                    throw new InvalidDataException(
                        $"{oldPos + size - _reader.Consumed} bytes left in server address encapsulation");
                }
            }
        }

        if (serverAddress is null)
        {
            throw new InvalidDataException(
                $"cannot decode server address for protocol '{protocol}' and transport '{transportCode.ToString().ToLowerInvariant()}' with server address encapsulation encoded with encoding '{encodingMajor}.{encodingMinor}'");
        }

        return serverAddress.Value;
    }

    private bool DecodeTagHeader(int tag, TagFormat expectedFormat, bool useTagEndMarker)
    {
        Debug.Assert(Encoding == SliceEncoding.Slice1);

        if (!useTagEndMarker && _classContext.Current.InstanceType != InstanceType.None)
        {
            throw new ArgumentException(
                $"{nameof(useTagEndMarker)} must be true when decoding a class/exception data members",
                nameof(useTagEndMarker));
        }
        else if (useTagEndMarker && _classContext.Current.InstanceType == InstanceType.None)
        {
            throw new ArgumentException(
                $"{nameof(useTagEndMarker)} must be false when decoding parameters",
                nameof(useTagEndMarker));
        }

        if (_classContext.Current.InstanceType != InstanceType.None)
        {
            // tagged member of a class or exception
            if ((_classContext.Current.SliceFlags & SliceFlags.HasTaggedMembers) == 0)
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
                if (expectedFormat == TagFormat.OVSize)
                {
                    expectedFormat = TagFormat.VSize; // fix virtual tag format
                }

                if (format != expectedFormat)
                {
                    throw new InvalidDataException($"invalid tagged parameter '{tag}': unexpected format");
                }
                return true;
            }
        }
    }

    private byte PeekByte() => _reader.TryPeek(out byte value) ? value : throw new EndOfBufferException();

    /// <summary>Helper method to decode a service address encoded with Slice1.</summary>
    /// <param name="path">The decoded path.</param>
    /// <returns>The decoded service address.</returns>
    private ServiceAddress DecodeServiceAddress(string path)
    {
        // With Slice1, a proxy is encoded as a kind of discriminated union with:
        // - Identity
        // - If Identity is not the null identity:
        //     - The fragment, invocation mode, protocol major and minor, and the
        //       encoding major and minor
        //     - a sequence of server addresses that can be empty
        //     - an adapter ID string present only when the sequence of server addresses is empty

        string fragment = FragmentSliceDecoderExtensions.DecodeFragment(ref this);
        _ = InvocationModeSliceDecoderExtensions.DecodeInvocationMode(ref this);
        _ = DecodeBool();
        byte protocolMajor = DecodeUInt8();
        byte protocolMinor = DecodeUInt8();
        Skip(2); // skip encoding major and minor

        if (protocolMajor == 0)
        {
            throw new InvalidDataException("received service address with protocol set to 0");
        }
        if (protocolMinor != 0)
        {
            throw new InvalidDataException(
                $"received service address with invalid protocolMinor value: {protocolMinor}");
        }

        int count = DecodeSize();

        ServerAddress? serverAddress = null;
        IEnumerable<ServerAddress> altServerAddresses = ImmutableList<ServerAddress>.Empty;
        var protocol = Protocol.FromByteValue(protocolMajor);
        ImmutableDictionary<string, string> serviceAddressParams = ImmutableDictionary<string, string>.Empty;

        if (count == 0)
        {
            if (DecodeString() is string adapterId && adapterId.Length > 0)
            {
                serviceAddressParams = serviceAddressParams.Add("adapter-id", adapterId);
            }
        }
        else
        {
            serverAddress = DecodeServerAddress(protocol);
            if (count >= 2)
            {
                // A slice1 encoded server address consumes at least 8 bytes (2 bytes for the server address type and 6 bytes
                // for the encapsulation header). SizeOf ServerAddress is large but less than 8 * 8.
                IncreaseCollectionAllocation(count * Unsafe.SizeOf<ServerAddress>());

                var serverAddressArray = new ServerAddress[count - 1];
                for (int i = 0; i < count - 1; ++i)
                {
                    serverAddressArray[i] = DecodeServerAddress(protocol);
                }
                altServerAddresses = serverAddressArray;
            }
        }

        try
        {
            if (!protocol.HasFragment && fragment.Length > 0)
            {
                throw new InvalidDataException($"unexpected fragment in {protocol} service address");
            }

            return new ServiceAddress(
                protocol,
                path,
                serverAddress,
                altServerAddresses.ToImmutableList(),
                serviceAddressParams,
                fragment);
        }
        catch (InvalidDataException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidDataException("received invalid service address", ex);
        }
    }

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
                    throw new InvalidDataException($"decoded invalid size: {size}");
                }
                Skip(size);
                break;
            default:
                throw new InvalidDataException(
                    $"cannot skip tagged parameter or data member with tag format '{format}'");
        }
    }

    /// <summary>The exception thrown when attempting to decode at/past the end of the buffer.</summary>
    private class EndOfBufferException : InvalidDataException
    {
        internal EndOfBufferException()
            : base("attempting to decode past the end of the decoder buffer")
        {
        }
    }
}
