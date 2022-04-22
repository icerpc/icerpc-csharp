// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using IceRpc.Transports.Internal;
using System.Buffers;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

using static IceRpc.Slice.Internal.Slice1Definitions;

namespace IceRpc.Slice
{
    /// <summary>Decodes a byte buffer encoded using the Slice encoding.</summary>
    public ref partial struct SliceDecoder
    {
        /// <summary>The Slice encoding decoded by this decoder.</summary>
        public SliceEncoding Encoding { get; }

        /// <summary>The number of bytes decoded in the underlying buffer.</summary>
        internal long Consumed => _reader.Consumed;

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

        // Connection used when decoding relative proxies.
        private readonly Connection? _connection;

        // The current depth when decoding a type recursively.
        private int _currentDepth;

        // Invoker used when decoding proxies.
        private readonly IInvoker _invoker;

        // The maximum depth when decoding a type recursively.
        private readonly int _maxDepth;

        // The sequence reader.
        private SequenceReader<byte> _reader;

        /// <summary>Constructs a new Slice decoder over a byte buffer.</summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="connection">The connection, used only when decoding relative proxies.</param>
        /// <param name="invoker">The invoker of proxies decoded by this decoder. Use null to get the default invoker.
        /// </param>
        /// <param name="activator">The optional activator.</param>
        /// <param name="maxDepth">The maximum depth when decoding a type recursively. <c>-1</c> uses the default.
        /// </param>
        public SliceDecoder(
            ReadOnlySequence<byte> buffer,
            SliceEncoding encoding,
            Connection? connection = null,
            IInvoker? invoker = null,
            IActivator? activator = null,
            int maxDepth = -1)
        {
            Encoding = encoding;

            _activator = activator ?? _defaultActivator;
            _classContext = default;
            _connection = connection;
            _currentDepth = 0;
            _invoker = invoker ?? Proxy.DefaultInvoker;

            _maxDepth = maxDepth == -1 ? 100 :
                (maxDepth >= 1 ? maxDepth :
                    throw new ArgumentException($"{nameof(maxDepth)} must be -1 or greater than 1", nameof(maxDepth)));

            _reader = new SequenceReader<byte>(buffer);
        }

        /// <summary>Constructs a new Slice decoder over a byte buffer.</summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="activator">The activator.</param>
        /// <param name="maxDepth">The maximum depth when decoding a type recursively. <c>-1</c> uses the default.
        /// </param>
        public SliceDecoder(
            ReadOnlyMemory<byte> buffer,
            SliceEncoding encoding,
            Connection? connection = null,
            IInvoker? invoker = null,
            IActivator? activator = null,
            int maxDepth = -1)
            : this(new ReadOnlySequence<byte>(buffer), encoding, connection, invoker, activator, maxDepth)
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

            if (instance == null)
            {
                return fallback != null ? fallback(typeId, ref this) :
                    throw new InvalidDataException($"activator could not find type with Slice type ID '{typeId}'");
            }
            else
            {
                return instance is T result ? result : throw new InvalidDataException(
                    $"decoded instance of type '{instance.GetType()}' does not implement '{typeof(T)}'");
            }
        }

        /// <summary>Decodes a nullable proxy.</summary>
        /// <param name="bitSequenceReader">The bit sequence reader, ignored with Slice1.</param>
        /// <returns>The decoded proxy, or null.</returns>
        public Proxy? DecodeNullableProxy(ref BitSequenceReader bitSequenceReader)
        {
            if (Encoding == SliceEncoding.Slice1)
            {
                string path = this.DecodeIdentityPath();
                return path != "/" ? DecodeProxy(path) : null;
            }
            else
            {
                return bitSequenceReader.Read() ? DecodeProxy() : null;
            }
        }

        /// <summary>Decodes a proxy.</summary>
        /// <returns>The decoded proxy</returns>
        public Proxy DecodeProxy()
        {
            if (Encoding == SliceEncoding.Slice1)
            {
                string path = this.DecodeIdentityPath();
                return path != "/" ? DecodeProxy(path) :
                    throw new InvalidDataException("decoded null for a non-nullable proxy");
            }
            else
            {
                string proxyString = DecodeString();
                try
                {
                    if (proxyString.StartsWith('/')) // relative proxy
                    {
                        if (_connection == null)
                        {
                            throw new InvalidOperationException(
                                "cannot decode a relative proxy from an decoder with a null Connection");
                        }
                        return Proxy.FromConnection(_connection, proxyString, _invoker);
                    }
                    else
                    {
                        var proxy = new Proxy(new Uri(proxyString, UriKind.Absolute));
                        if (proxy.Protocol.IsSupported)
                        {
                            proxy.Invoker = _invoker;
                        }
                        return proxy;
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("received invalid proxy", ex);
                }
            }
        }

        // Other methods

        /// <summary>Copy bytes from the underlying reader into the destination to fill completely destination.
        /// </summary>
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

        /// <summary>Decodes a Slice2 encoded tagged parameter or data member.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="decodeFunc">A decode function that decodes the value of this tag.</param>
        /// <returns>The decoded value of the tagged parameter or data member, or null if not found.</returns>
        /// <remarks>When T is a value type, it should be a nullable value type such as int?.</remarks>
        public T DecodeTagged<T>(int tag, DecodeFunc<T> decodeFunc)
        {
            if (Encoding == SliceEncoding.Slice1)
            {
                throw new InvalidOperationException("Slice1 encoded tags must be decoded with tag formats");
            }

            int requestedTag = tag;

            // For decoding parameters, return values, and exception data members we rely on the end of the buffer
            // to detect the end of the tag 'dictionary'. Struct data members use TagEndMarker.
            while (!_reader.End)
            {
                long startPos = _reader.Consumed;
                tag = DecodeVarInt32();

                if (tag == requestedTag)
                {
                    // Found requested tag, so skip size:
                    SkipSize();
                    return decodeFunc(ref this);
                }
                else if (tag == Slice2Definitions.TagEndMarker || tag > requestedTag)
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
            return default!;
        }

        /// <summary>Decodes a Slice1 encoded tagged parameter or data member.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="tagFormat">The expected tag format of this tag when found in the underlying buffer.</param>
        /// <param name="decodeFunc">A decode function that decodes the value of this tag.</param>
        /// <returns>The decoded value of the tagged parameter or data member, or null if not found.</returns>
        /// <remarks>When T is a value type, it should be a nullable value type such as int?.</remarks>
        public T DecodeTagged<T>(int tag, TagFormat tagFormat, DecodeFunc<T> decodeFunc)
        {
            if (Encoding != SliceEncoding.Slice1)
            {
                throw new InvalidOperationException("tag formats can only be used with the Slice1 encoding");
            }

            if (DecodeTaggedParamHeader(tag, tagFormat))
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

        /// <summary>Decodes a Slice1 system exception.</summary>
        public DispatchException DecodeSystemException()
        {
            if (Encoding != SliceEncoding.Slice1)
            {
                throw new InvalidOperationException(
                    $"{nameof(DecodeSystemException)} is not compatible with {Encoding}");
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

                        message = @$"{nameof(DispatchException)} {{ ErrorCode = {errorCode} }} while dispatching '{requestFailed.Operation}' on '{target}'";
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

        /// <summary>Gets a bit sequence reader to read the underlying bit sequence later on.</summary>
        /// <param name="bitSequenceSize">The minimum number of bits in the sequence.</param>
        /// <returns>A bit sequence reader.</returns>

        public BitSequenceReader GetBitSequenceReader(int bitSequenceSize)
        {
            if (Encoding == SliceEncoding.Slice1)
            {
                return default;
            }
            else
            {
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
        }

        // Applies to all var type: varint62, varuint62 etc.
        internal static int DecodeVarInt62Length(byte from) => 1 << (from & 0x03);

        /// <summary>Verifies the Slice decoder has reached the end of its underlying buffer.</summary>
        /// <param name="skipTaggedParams">When true, first skips all remaining tagged parameters in the current
        /// buffer.</param>
        internal void CheckEndOfBuffer(bool skipTaggedParams)
        {
            if (skipTaggedParams)
            {
                SkipTaggedParams();
            }

            if (!_reader.End)
            {
                throw new InvalidDataException($"{_reader.Remaining} bytes remaining in the buffer");
            }
        }

        /// <summary>Decodes a dictionary size and makes sure there is enough space in the underlying buffer to decode
        /// the dictionary. This validation is performed to make sure we do not allocate a large dictionary based on an
        /// invalid encoded size.</summary>
        /// <param name="minKeySize">The minimum encoded size of a key, in bytes.</param>
        /// <param name="minValueSize">The minimum encoded size of a value, in bytes. It's 0 for values with an optional
        /// type.</param>
        /// <returns>The number of elements in the dictionary.</returns>
        internal int DecodeAndCheckDictionarySize(int minKeySize, int minValueSize)
        {
            if (minKeySize <= 0)
            {
                throw new ArgumentException($"{nameof(minKeySize)} must be greater than 0", nameof(minKeySize));
            }

            Debug.Assert(minValueSize >= 0);

            int size = DecodeSize();

            if (size == 0)
            {
                return 0;
            }

            int minSize = (size * minKeySize) +
                (minValueSize > 0 ? size * minValueSize : SliceEncoder.GetBitSequenceByteCount(size));

            return _reader.Remaining >= minSize ? size : throw new InvalidDataException("invalid dictionary size");
        }

        /// <summary>Decodes a sequence size and makes sure there is enough space in the underlying buffer to decode the
        /// sequence. This validation is performed to make sure we do not allocate a large container based on an
        /// invalid encoded size.</summary>
        /// <param name="minElementSize">The minimum encoded size of an element of the sequence, in bytes. It's 0 for an
        /// optional type.</param>
        /// <returns>The number of elements in the sequence.</returns>
        internal int DecodeAndCheckSequenceSize(int minElementSize)
        {
            Debug.Assert(minElementSize >= 0);

            int size = DecodeSize();

            if (size == 0)
            {
                return 0;
            }

            int minSize = minElementSize > 0 ? size * minElementSize : SliceEncoder.GetBitSequenceByteCount(size);

            return _reader.Remaining >= minSize ? size : throw new InvalidDataException("invalid sequence size");
        }

        /// <summary>Decodes fields.</summary>
        /// <returns>The fields.</returns>
        /// <remarks>The fields use and must use the remainder of the underlying buffer.</remarks>
        internal IDictionary<TKey, ReadOnlySequence<byte>> DecodeFieldDictionary<TKey>(
            DecodeFunc<TKey> decodeKeyFunc) where TKey : struct
        {
            int size = DecodeSize();
            if (size == 0)
            {
                return ImmutableDictionary<TKey, ReadOnlySequence<byte>>.Empty;
            }
            else
            {
                // TODO: for now we make a copy of the remaining bytes in _reader into a new buffer. Ideally, we would
                // use _reader directly but this requires a separate "backing" Pipe.
                byte[] buffer = new byte[_reader.Remaining];
                _ = _reader.TryCopyTo(buffer);
                _reader.AdvanceToEnd();

                var decoder = new SliceDecoder(new ReadOnlyMemory<byte>(buffer), Encoding);
                var dict = new Dictionary<TKey, ReadOnlySequence<byte>>(size);
                for (int i = 0; i < size; ++i)
                {
                    TKey key = decodeKeyFunc(ref decoder);
                    int valueSize = decoder.DecodeSize();
                    ReadOnlySequence<byte> value = decoder._reader.UnreadSequence.Slice(0, valueSize);
                    decoder._reader.Advance(valueSize);
                    dict.Add(key, value);
                }
                return dict;
            }
        }

        /// <summary>Decodes a size encoded on a fixed number of bytes.</summary>
        /// <returns>The size decoded by this decoder.</returns>
        internal int DecodeFixedLengthSize()
        {
            if (Encoding == SliceEncoding.Slice1)
            {
                int size = DecodeInt32();
                if (size < 0)
                {
                    throw new InvalidDataException($"decoded invalid size: {size}");
                }
                return size;
            }
            else
            {
                return DecodeSize();
            }
        }

        internal void Skip(int count)
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

        internal void SkipSize()
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

        /// <summary>Tries to decode a Slice uint8 into a byte.</summary>
        /// <param name="value">When this method returns <c>true</c>, this value is set to the decoded byte. Otherwise,
        /// this value is set to its default value.</param>
        /// <returns><c>true</c> if the decoder is not at the end of the buffer and the decode operation succeeded;
        /// <c>false</c> otherwise.</returns>
        internal bool TryDecodeUInt8(out byte value) => _reader.TryRead(out value);

        /// <summary>Tries to decode a Slice int32 into an int.</summary>
        /// <param name="value">When this method returns <c>true</c>, this value is set to the decoded int. Otherwise,
        /// this value is set to its default value.</param>
        /// <returns><c>true</c> if the decoder is not at the end of the buffer and the decode operation succeeded;
        /// <c>false</c> otherwise.</returns>
        internal bool TryDecodeInt32(out int value) => SequenceMarshal.TryRead(ref _reader, out value);

        /// <summary>Tries to decode a size encoded on a variable number of bytes.</summary>
        /// <param name="size">When this method returns <c>true</c>, this value is set to the decoded size. Otherwise,
        /// this value is set to its default value.</param>
        /// <returns><c>true</c> if the decoder is not at the end of the buffer and the decode operation succeeded;
        /// <c>false</c> otherwise.</returns>
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
        /// <param name="value">When this method returns <c>true</c>, this value is set to the decoded ulong. Otherwise,
        /// this value is set to its default value.</param>
        /// <returns><c>true</c> if the decoder is not at the end of the buffer and the decode operation succeeded;
        /// <c>false</c> otherwise.</returns>
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

        /// <summary>Decodes an endpoint (Slice1).</summary>
        /// <param name="protocol">The protocol of this endpoint.</param>
        /// <returns>The endpoint decoded by this decoder.</returns>
        private Endpoint DecodeEndpoint(Protocol protocol)
        {
            Debug.Assert(Encoding == SliceEncoding.Slice1);

            // The Slice1 ice endpoints are transport-specific, and hard-coded here and in the
            // SliceEncoder. The preferred and fallback encoding for new transports is TransportCode.Uri.

            Endpoint? endpoint = null;
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
                            endpoint = Transports.TcpClientTransport.DecodeEndpoint(
                                ref this,
                                transportCode == TransportCode.Tcp ? TransportNames.Tcp : TransportNames.Ssl);
                            break;
                        }

                        case TransportCode.Uri:
                            endpoint = Endpoint.FromString(DecodeString());
                            if (endpoint.Value.Protocol != protocol)
                            {
                                throw new InvalidDataException(
                                    $"expected endpoint for {protocol} but received '{endpoint.Value}'");
                            }
                            break;

                        default:
                        {
                            // Create an endpoint for transport opaque

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
                            builder.Add("transport", TransportNames.Opaque);
                            builder.Add("t", ((short)transportCode).ToString(CultureInfo.InvariantCulture));
                            builder.Add("v", Convert.ToBase64String(vSpan));

                            endpoint = new Endpoint(
                                Protocol.Ice,
                                OpaqueTransport.Host,
                                OpaqueTransport.Port,
                                builder.ToImmutable());
                            break;
                        }
                    }
                }
                else if (transportCode == TransportCode.Uri)
                {
                    // The endpoints of Slice1 encoded icerpc proxies only use TransportCode.Uri.

                    endpoint = Endpoint.FromString(DecodeString());
                    if (endpoint.Value.Protocol != protocol)
                    {
                        throw new InvalidDataException(
                            $"expected {protocol} endpoint but received '{endpoint.Value}'");
                    }
                }

                if (endpoint != null)
                {
                    // Make sure we read the full encapsulation.
                    if (_reader.Consumed != oldPos + size)
                    {
                        throw new InvalidDataException(
                            $"{oldPos + size - _reader.Consumed} bytes left in endpoint encapsulation");
                    }
                }
            }

            if (endpoint == null)
            {
                throw new InvalidDataException(
                    @$"cannot decode endpoint for protocol '{protocol}' and transport '{transportCode.ToString().ToLowerInvariant()}' with endpoint encapsulation encoded with encoding '{encodingMajor}.{encodingMinor}'");
            }

            return endpoint.Value;
        }

        /// <summary>Determines if a tagged parameter or data member is available.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="expectedFormat">The expected format of the tagged parameter.</param>
        /// <returns>True if the tagged parameter is present; otherwise, false.</returns>
        private bool DecodeTaggedParamHeader(int tag, TagFormat expectedFormat)
        {
            Debug.Assert(Encoding == SliceEncoding.Slice1);

            bool withTagEndMarker = false;

            if (_classContext.Current.InstanceType != InstanceType.None)
            {
                // tagged member of a class or exception
                if ((_classContext.Current.SliceFlags & SliceFlags.HasTaggedMembers) == 0)
                {
                    // The current slice has no tagged parameter.
                    return false;
                }
                withTagEndMarker = true;
            }

            int requestedTag = tag;

            while (true)
            {
                if (!withTagEndMarker && _reader.End)
                {
                    return false; // End of buffer indicates end of tagged parameters.
                }

                long savedPos = _reader.Consumed;

                int v = DecodeUInt8();
                if (withTagEndMarker && v == TagEndMarker)
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

        /// <summary>Skips the remaining tagged parameters, return value _or_ data members.</summary>
        public void SkipTaggedParams()
        {
            if (Encoding == SliceEncoding.Slice1)
            {
                bool withTagEndMarker = _classContext.Current.InstanceType != InstanceType.None;

                while (true)
                {
                    if (!withTagEndMarker && _reader.End)
                    {
                        // When we don't use an end marker, the end of the buffer indicates the end of the tagged params
                        // or members.
                        break;
                    }

                    int v = DecodeUInt8();
                    if (withTagEndMarker && v == TagEndMarker)
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
            else
            {
                // For decoding parameters, return values, and exception data members we rely on the end of the buffer
                // to detect the end of the tag 'dictionary'. Struct data members use TagEndMarker.
                while (!_reader.End)
                {
                    // Read the next tag and skip it. If we read the tag end marker, exit the loop.
                    if (DecodeVarInt32() == Slice2Definitions.TagEndMarker)
                    {
                        break; // while
                    }

                    // Skip tagged value
                    Skip(DecodeSize());
                }
            }
        }

        /// <summary>Helper method to decode a proxy encoded with Slice1.</summary>
        /// <param name="path">The decoded path.</param>
        /// <returns>The decoded proxy.</returns>
        private Proxy DecodeProxy(string path)
        {
            var proxyData = new ProxyData(ref this);

            if (proxyData.ProtocolMajor == 0)
            {
                throw new InvalidDataException("received proxy with protocol set to 0");
            }
            if (proxyData.ProtocolMinor != 0)
            {
                throw new InvalidDataException(
                    $"received proxy with invalid protocolMinor value: {proxyData.ProtocolMinor}");
            }

            // The min size for an Endpoint with Slice1 is: transport (short = 2 bytes) + encapsulation
            // header (6 bytes), for a total of 8 bytes.
            int size = DecodeAndCheckSequenceSize(8);

            Endpoint? endpoint = null;
            IEnumerable<Endpoint> altEndpoints = ImmutableList<Endpoint>.Empty;
            var protocol = Protocol.FromByte(proxyData.ProtocolMajor);
            ImmutableDictionary<string, string> proxyParams = ImmutableDictionary<string, string>.Empty;

            if (size == 0)
            {
                if (DecodeString() is string adapterId && adapterId.Length > 0)
                {
                    proxyParams = proxyParams.Add("adapter-id", adapterId);
                }
            }
            else
            {
                endpoint = DecodeEndpoint(protocol);
                if (size >= 2)
                {
                    var endpointArray = new Endpoint[size - 1];
                    for (int i = 0; i < size - 1; ++i)
                    {
                        endpointArray[i] = DecodeEndpoint(protocol);
                    }
                    altEndpoints = endpointArray;
                }
            }

            try
            {
                if (!protocol.HasFragment && proxyData.Fragment.Length > 0)
                {
                    throw new InvalidDataException($"unexpected fragment in {protocol} proxy");
                }

                return new Proxy(
                    protocol,
                    path,
                    endpoint,
                    altEndpoints.ToImmutableList(),
                    proxyParams,
                    proxyData.Fragment,
                    _invoker);
            }
            catch (InvalidDataException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("received invalid proxy", ex);
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
}
