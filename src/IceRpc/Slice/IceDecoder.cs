// Copyright (c) ZeroC, Inc. All rights reserved.

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

using static IceRpc.Slice.Internal.Slice11Definitions;

namespace IceRpc.Slice
{
    /// <summary>Decodes a byte buffer encoded using the Ice encoding.</summary>
    public ref partial struct IceDecoder
    {
        /// <summary>The Slice encoding decoded by this decoder.</summary>
        public IceEncoding Encoding { get; }

        /// <summary>The number of bytes decoded in the underlying buffer.</summary>
        internal long Consumed => _reader.Consumed;

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

        private readonly IActivator? _activator;

        private ClassContext _classContext;

        // Connection used when decoding proxies.
        private readonly Connection? _connection;

        // Invoker used when decoding proxies.
        private readonly IInvoker _invoker;

        // The sum of all the minimum sizes (in bytes) of the sequences decoded from this buffer. Must not exceed the
        // buffer size.
        private int _minTotalSeqSize;

        // The sequence reader.
        private SequenceReader<byte> _reader;

        /// <summary>Constructs a new Ice decoder over a byte buffer.</summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker of proxies decoded by this decoder. Use null to get the default invoker.
        /// </param>
        /// <param name="activator">The activator.</param>
        /// <param name="classGraphMaxDepth">The class graph max depth.</param>
        public IceDecoder(
            ReadOnlySequence<byte> buffer,
            IceEncoding encoding,
            Connection? connection = null,
            IInvoker? invoker = null,
            IActivator? activator = null,
            int classGraphMaxDepth = -1)
        {
            Encoding = encoding;

            _activator = activator;
            _classContext = new ClassContext(classGraphMaxDepth);
            _connection = connection;
            _invoker = invoker ?? Proxy.DefaultInvoker;
            _minTotalSeqSize = 0;
            _reader = new SequenceReader<byte>(buffer);
        }

        /// <summary>Constructs a new Ice decoder over a byte buffer.</summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="activator">The activator.</param>
        /// <param name="classGraphMaxDepth">The class graph max depth.</param>
        public IceDecoder(
            ReadOnlyMemory<byte> buffer,
            IceEncoding encoding,
            Connection? connection = null,
            IInvoker? invoker = null,
            IActivator? activator = null,
            int classGraphMaxDepth = -1)
            : this(new ReadOnlySequence<byte>(buffer), encoding, connection, invoker, activator, classGraphMaxDepth)
        {
        }

        // Decode methods for basic types

        /// <summary>Decodes a bool.</summary>
        /// <returns>The bool decoded by this decoder.</returns>
        public bool DecodeBool() =>
            _reader.TryRead(out byte value) ? value != 0 : throw new EndOfBufferException();

        /// <summary>Decodes a byte.</summary>
        /// <returns>The byte decoded by this decoder.</returns>
        public byte DecodeByte() =>
            _reader.TryRead(out byte value) ? value : throw new EndOfBufferException();

        /// <summary>Decodes a double.</summary>
        /// <returns>The double decoded by this decoder.</returns>
        public double DecodeDouble() =>
            SequenceMarshal.TryRead(ref _reader, out double value) ? value : throw new EndOfBufferException();

        /// <summary>Decodes a float.</summary>
        /// <returns>The float decoded by this decoder.</returns>
        public float DecodeFloat() =>
            SequenceMarshal.TryRead(ref _reader, out float value) ? value : throw new EndOfBufferException();

        /// <summary>Decodes an int.</summary>
        /// <returns>The int decoded by this decoder.</returns>
        public int DecodeInt() =>
            SequenceMarshal.TryRead(ref _reader, out int value) ? value : throw new EndOfBufferException();

        /// <summary>Decodes a long.</summary>
        /// <returns>The long decoded by this decoder.</returns>
        public long DecodeLong() =>
            SequenceMarshal.TryRead(ref _reader, out long value) ? value : throw new EndOfBufferException();

        /// <summary>Decodes a short.</summary>
        /// <returns>The short decoded by this decoder.</returns>
        public short DecodeShort() =>
            SequenceMarshal.TryRead(ref _reader, out short value) ? value : throw new EndOfBufferException();

        /// <summary>Decodes a size encoded on a variable number of bytes.</summary>
        /// <returns>The size decoded by this decoder.</returns>
        public int DecodeSize()
        {
            // The implementation does not use any internal or private property and therefore DecodeSize could be an
            // extension method. It's an instance method because it's considered fundamental.

            if (Encoding == IceRpc.Encoding.Slice11)
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
                        throw new InvalidDataException($"decoded invalid size: {size}");
                    }
                    return size;
                }
            }
            else
            {
                try
                {
                    return checked((int)DecodeVarULong());
                }
                catch (OverflowException ex)
                {
                    throw new InvalidDataException("cannot decode size larger than int.MaxValue", ex);
                }
            }
        }

        /// <summary>Decodes a string.</summary>
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
                    result = _utf8.GetString(_reader.UnreadSpan[0..size]);
                }
                else
                {
                    ReadOnlySequence<byte> bytes = _reader.UnreadSequence;
                    if (size > bytes.Length)
                    {
                        throw new EndOfBufferException();
                    }
                    result = _utf8.GetString(bytes.Slice(0, size));
                }

                _reader.Advance(size);
                return result;
            }
        }

        /// <summary>Decodes a uint.</summary>
        /// <returns>The uint decoded by this decoder.</returns>
        public uint DecodeUInt() =>
            SequenceMarshal.TryRead(ref _reader, out uint value) ? value : throw new EndOfBufferException();

        /// <summary>Decodes a ulong.</summary>
        /// <returns>The ulong decoded by this decoder.</returns>
        public ulong DecodeULong() =>
            SequenceMarshal.TryRead(ref _reader, out ulong value) ? value : throw new EndOfBufferException();

        /// <summary>Decodes a ushort.</summary>
        /// <returns>The ushort decoded by this decoder.</returns>
        public ushort DecodeUShort() =>
            SequenceMarshal.TryRead(ref _reader, out ushort value) ? value : throw new EndOfBufferException();

        /// <summary>Decodes an int. This int is encoded using Ice's variable-size integer encoding.
        /// </summary>
        /// <returns>The int decoded by this decoder.</returns>
        public int DecodeVarInt()
        {
            try
            {
                return checked((int)DecodeVarLong());
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException("varint value is out of range", ex);
            }
        }

        /// <summary>Decodes a long. This long is encoded using Ice's variable-size integer encoding.
        /// </summary>
        /// <returns>The long decoded by this decoder.</returns>
        public long DecodeVarLong() =>
            (PeekByte() & 0x03) switch
            {
                0 => (sbyte)DecodeByte() >> 2,
                1 => DecodeShort() >> 2,
                2 => DecodeInt() >> 2,
                _ => DecodeLong() >> 2
            };

        /// <summary>Decodes a uint. This uint is encoded using Ice's variable-size integer encoding.
        /// </summary>
        /// <returns>The uint decoded by this decoder.</returns>
        public uint DecodeVarUInt()
        {
            try
            {
                return checked((uint)DecodeVarULong());
            }
            catch (OverflowException ex)
            {
                throw new InvalidDataException("varuint value is out of range", ex);
            }
        }

        /// <summary>Decodes a ulong. This ulong is encoded using Ice's variable-size integer encoding.
        /// </summary>
        /// <returns>The ulong decoded by this decoder.</returns>
        public ulong DecodeVarULong() =>
            (PeekByte() & 0x03) switch
            {
                0 => (uint)DecodeByte() >> 2,   // cast to uint to use operator >> for uint instead of int, which is
                1 => (uint)DecodeUShort() >> 2, // later implicitly converted to ulong
                2 => DecodeUInt() >> 2,
                _ => DecodeULong() >> 2
            };

        // Decode methods for constructed types

        /// <summary>Decodes a remote exception.</summary>
        /// <returns>The remote exception.</returns>
        public RemoteException DecodeException()
        {
            if (Encoding == IceRpc.Encoding.Slice11)
            {
                return DecodeExceptionClass();
            }
            else
            {
                string typeId = DecodeString();

                if (_activator?.CreateInstance(typeId, ref this) is RemoteException remoteException)
                {
                    // TODO: consider calling this Skip for the remaining exception tagged members from the generated
                    // code to make the exception decoding constructor usable directly. See protocol bridging code.
                    SkipTaggedParams();
                    return remoteException;
                }
                else
                {
                    // If we can't decode this exception, we return an UnknownSlicedRemoteException instead of throwing
                    // "can't decode remote exception".
                    return new UnknownSlicedRemoteException(typeId, ref this);
                }
            }
        }

        /// <summary>Decodes a nullable proxy.</summary>
        /// <returns>The decoded proxy, or null.</returns>
        public Proxy? DecodeNullableProxy()
        {
            if (_connection == null)
            {
                throw new InvalidOperationException("cannot decode a proxy from an decoder with a null Connection");
            }

            if (Encoding == IceRpc.Encoding.Slice11)
            {
                var identity = new Identity(ref this);
                if (identity.Name.Length == 0) // null proxy
                {
                    return null;
                }

                var proxyData = new ProxyData11(ref this);

                if (proxyData.ProtocolMajor == 0)
                {
                    throw new InvalidDataException("received proxy with protocol set to 0");
                }
                if (proxyData.ProtocolMinor != 0)
                {
                    throw new InvalidDataException(
                        $"received proxy with invalid protocolMinor value: {proxyData.ProtocolMinor}");
                }

                // The min size for an Endpoint with the 1.1 encoding is: transport (short = 2 bytes) + encapsulation
                // header (6 bytes), for a total of 8 bytes.
                int size = DecodeAndCheckSeqSize(8);

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

                proxyData.Facet.CheckValue();

                if (protocol == Protocol.Ice)
                {
                    try
                    {
                        return new Proxy(identity.ToPath(), Protocol.Ice)
                        {
                            Encoding = IceRpc.Encoding.FromMajorMinor(
                                proxyData.EncodingMajor,
                                proxyData.EncodingMinor),
                            Endpoint = endpoint,
                            AltEndpoints = altEndpoints.ToImmutableList(),
                            Invoker = _invoker,
                            Fragment = proxyData.Facet.ToFragment(),
                            Params = proxyParams
                        };
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
                else
                {
                    if (proxyData.InvocationMode != InvocationMode.Twoway)
                    {
                        throw new InvalidDataException(
                            $"received proxy for protocol {protocol} with invocation mode set");
                    }

                    try
                    {
                        Proxy proxy;

                        if (endpoint == null)
                        {
                            proxy = Proxy.FromConnection(_connection!, identity.ToPath(), _invoker);
                        }
                        else
                        {
                            proxy = new Proxy(identity.ToPath(), protocol)
                            {
                                Endpoint = endpoint,
                                AltEndpoints = altEndpoints.ToImmutableList(),
                                Invoker = _invoker
                            };
                        }

                        proxy.Fragment = proxyData.Facet.ToFragment();

                        proxy.Encoding = IceRpc.Encoding.FromMajorMinor(proxyData.EncodingMajor,
                            proxyData.EncodingMinor);

                        // TODO: revisit with relative proxy decoding
                        if (proxy.Endpoint == null)
                        {
                            proxy.Params = proxyParams;
                        }

                        return proxy;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidDataException("received invalid proxy", ex);
                    }
                }
            }
            else
            {
                var proxyData = new ProxyData20(ref this);

                if (proxyData.Path == null)
                {
                    return null;
                }

                Protocol protocol = proxyData.Protocol != null ? Protocol.FromString(proxyData.Protocol) : Protocol.IceRpc;
                Endpoint? endpoint = proxyData.Endpoint is EndpointData data ? data.ToEndpoint() : null;
                ImmutableList<Endpoint> altEndpoints =
                    proxyData.AltEndpoints?.Select(data => data.ToEndpoint()).ToImmutableList() ??
                        ImmutableList<Endpoint>.Empty;

                if (endpoint == null && altEndpoints.Count > 0)
                {
                    throw new InvalidDataException("received proxy with only alt endpoints");
                }

                try
                {
                    Proxy proxy;

                    Debug.Assert(proxyData.Fragment != null);

                    if (endpoint == null && protocol != Protocol.Ice)
                    {
                        proxy = Proxy.FromConnection(_connection!, proxyData.Path, _invoker);
                    }
                    else
                    {
                        proxy = new Proxy(proxyData.Path, protocol)
                        {
                            Endpoint = endpoint,
                            AltEndpoints = altEndpoints,
                            Invoker = _invoker
                        };
                    }

                    proxy.Fragment = proxyData.Fragment;
                    proxy.Encoding = proxyData.Encoding is string encoding ?
                        IceRpc.Encoding.FromString(encoding) : protocol.SliceEncoding ?? IceRpc.Encoding.Unknown;
                    proxy.Params = proxyData.Params?.ToImmutableDictionary() ?? proxy.Params;

                    return proxy;
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("received invalid proxy", ex);
                }
            }
        }

        /// <summary>Decodes a proxy.</summary>
        /// <returns>The decoded proxy</returns>
        public Proxy DecodeProxy() =>
            DecodeNullableProxy() ?? throw new InvalidDataException("decoded null for a non-nullable proxy");

        /// <summary>Decodes a sequence of fixed-size numeric values and returns an array.</summary>
        /// <param name="checkElement">A delegate used to check each element of the array (optional).</param>
        /// <returns>The sequence decoded by this decoder, as an array.</returns>
        public T[] DecodeSequence<T>(Action<T>? checkElement = null) where T : struct
        {
            int elementSize = Unsafe.SizeOf<T>();
            var value = new T[DecodeAndCheckSeqSize(elementSize)];

            Span<byte> destination = MemoryMarshal.Cast<T, byte>(value);
            Debug.Assert(destination.Length == elementSize * value.Length);
            CopyTo(destination);

            if (checkElement != null)
            {
                foreach (T e in value)
                {
                    checkElement(e);
                }
            }

            return value;
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

        /// <summary>Decodes a tagged parameter or data member.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="tagFormat">The expected tag format of this tag when found in the underlying buffer.</param>
        /// <param name="decodeFunc">A decode function that decodes the value of this tag.</param>
        /// <returns>The decoded value of the tagged parameter or data member, or null if not found.</returns>
        /// <remarks>When T is a value type, it should be a nullable value type such as int?.</remarks>
        public T DecodeTagged<T>(int tag, TagFormat tagFormat, DecodeFunc<T> decodeFunc)
        {
            if (Encoding == IceRpc.Encoding.Slice11)
            {
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
            else
            {
                // TODO: the current version is for paramaters, return values and exception data members. It relies on
                // the end of buffer to detect the end of the tag "dictionary", and does not use TagEndMarker.

                int requestedTag = tag;

                while (!_reader.End)
                {
                    long startPos = _reader.Consumed;
                    tag = DecodeVarInt();

                    if (tag == requestedTag)
                    {
                        // Found requested tag, so skip size:
                        SkipSize();
                        return decodeFunc(ref this);
                    }
                    else if (tag > requestedTag)
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
                    "bitSequenceSize must be greater than 0");
            }

            int size = (bitSequenceSize >> 3) + ((bitSequenceSize & 0x07) != 0 ? 1 : 0);
            ReadOnlySequence<byte> bitSequence = _reader.UnreadSequence.Slice(0, size);
            _reader.Advance(size);
            Debug.Assert(bitSequence.Length == size);
            return new BitSequenceReader(bitSequence);
        }

        internal static int DecodeInt(ReadOnlySpan<byte> from) => BitConverter.ToInt32(from);

        // Applies to all var type: varlong, varulong etc.
        internal static int DecodeVarLongLength(byte from) => 1 << (from & 0x03);

        internal static (ulong Value, int ValueLength) DecodeVarULong(ReadOnlySpan<byte> from)
        {
            ulong value = (from[0] & 0x03) switch
            {
                0 => (uint)from[0] >> 2,
                1 => (uint)BitConverter.ToUInt16(from) >> 2,
                2 => BitConverter.ToUInt32(from) >> 2,
                _ => BitConverter.ToUInt64(from) >> 2
            };

            return (value, DecodeVarLongLength(from[0]));
        }

        /// <summary>Verifies the Ice decoder has reached the end of its underlying buffer.</summary>
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

        /// <summary>Decodes a sequence size and makes sure there is enough space in the underlying buffer to decode the
        /// sequence. This validation is performed to make sure we do not allocate a large container based on an
        /// invalid encoded size.</summary>
        /// <param name="minElementSize">The minimum encoded size of an element of the sequence, in bytes. This value is
        /// 0 for sequence of nullable types other than mapped Slice classes and proxies.</param>
        /// <returns>The number of elements in the sequence.</returns>
        internal int DecodeAndCheckSeqSize(int minElementSize)
        {
            int size = DecodeSize();

            if (size == 0)
            {
                return 0;
            }

            // When minElementSize is 0, we only count of bytes that hold the bit sequence.
            int minSize = minElementSize > 0 ? size * minElementSize : (size >> 3) + ((size & 0x07) != 0 ? 1 : 0);

            // With _minTotalSeqSize, we make sure that multiple sequences within a buffer can't trigger maliciously
            // the allocation of a large amount of memory before we decode these sequences.
            _minTotalSeqSize += minSize;

            if (_reader.Remaining < minSize || _minTotalSeqSize > _reader.Length)
            {
                throw new InvalidDataException("invalid sequence size");
            }
            return size;
        }

        /// <summary>Decodes a size encoded on a fixed number of bytes.</summary>
        /// <returns>The size decoded by this decoder.</returns>
        internal int DecodeFixedLengthSize()
        {
            if (Encoding == IceRpc.Encoding.Slice11)
            {
                int size = DecodeInt();
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
            if (Encoding == IceRpc.Encoding.Slice11)
            {
                byte b = DecodeByte();
                if (b == 255)
                {
                    Skip(4);
                }
            }
            else
            {
                Skip(DecodeVarLongLength(PeekByte()));
            }
        }

        /// <summary>Decodes an endpoint (Slice 1.1).</summary>
        /// <param name="protocol">The Ice protocol of this endpoint.</param>
        /// <returns>The endpoint decoded by this decoder.</returns>
        private Endpoint DecodeEndpoint(Protocol protocol)
        {
            Debug.Assert(Encoding == IceRpc.Encoding.Slice11);

            // The Ice 1.1 encoding of ice endpoints is transport-specific, and hard-coded here and in the
            // IceEncoder. The preferred and fallback encoding for new transports is TransportCode.Any, which uses an
            // EndpointData like Ice 2.0.

            Debug.Assert(_connection != null);

            Endpoint? endpoint = null;
            TransportCode transportCode = this.DecodeTransportCode();

            int size = DecodeInt();
            if (size < 6)
            {
                throw new InvalidDataException($"the 1.1 encapsulation's size ({size}) is too small");
            }

            if (size - 4 > _reader.Remaining)
            {
                throw new InvalidDataException(
                    $"the encapsulation's size ({size}) extends beyond the end of the buffer");
            }

            // Remove 6 bytes from the encapsulation size (4 for encapsulation size, 2 for encoding).
            size -= 6;

            var encoding = IceRpc.Encoding.FromMajorMinor(DecodeByte(), DecodeByte());

            if (encoding == IceRpc.Encoding.Slice11 || encoding == IceRpc.Encoding.Slice10)
            {
                long oldPos = _reader.Consumed;

                if (protocol == Protocol.Ice)
                {
                    switch (transportCode)
                    {
                        case TransportCode.TCP:
                        case TransportCode.SSL:
                        {
                            string host = DecodeString();
                            ushort port = checked((ushort)DecodeInt());
                            int timeout = DecodeInt();
                            bool compress = DecodeBool();

                            ImmutableDictionary<string, string>.Builder builder =
                                ImmutableDictionary.CreateBuilder<string, string>();

                            builder.Add("transport", TransportNames.Tcp);

                            builder.Add(
                                    "tls",
                                    transportCode == TransportCode.SSL ? "true" : "false");

                            if (timeout != EndpointParseExtensions.DefaultTcpTimeout)
                            {
                                builder.Add(
                                    "t",
                                    timeout.ToString(CultureInfo.InvariantCulture));
                            }
                            if (compress)
                            {
                                builder.Add("z", "true");
                            }

                            endpoint = new Endpoint(
                                Protocol.Ice,
                                host,
                                port,
                                builder.ToImmutable());

                            break;
                        }

                        case TransportCode.UDP:
                        {
                            string host = DecodeString();
                            ushort port = checked((ushort)DecodeInt());
                            bool compress = DecodeBool();

                            ImmutableDictionary<string, string>.Builder builder =
                                ImmutableDictionary.CreateBuilder<string, string>();

                            builder.Add("transport", TransportNames.Udp);
                            if (compress)
                            {
                                builder.Add("z", "true");
                            }

                            endpoint = new Endpoint(
                                Protocol.Ice,
                                host,
                                port,
                                builder.ToImmutable());

                            // else endpoint remains null and we throw below
                            break;
                        }

                        case TransportCode.Any:
                            endpoint = new EndpointData(ref this).ToEndpoint();
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
                            builder.Add("transport", "opaque");
                            builder.Add("t", ((short)transportCode).ToString(CultureInfo.InvariantCulture));
                            builder.Add("e", encoding.ToString() );
                            builder.Add("v", Convert.ToBase64String(vSpan));

                            endpoint = new Endpoint(Protocol.Ice, host: "", port: 0, builder.ToImmutable());
                            break;
                        }
                    }
                }
                else if (transportCode == TransportCode.Any)
                {
                    endpoint = new EndpointData(ref this).ToEndpoint();
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

            string transportName = endpoint != null && endpoint.Params.TryGetValue("transport", out string? value) ?
                value : transportCode.ToString().ToLowerInvariant();

            return endpoint ??
                throw new InvalidDataException(
                    @$"cannot decode endpoint for protocol '{protocol}' and transport '{transportName
                    }' with endpoint encapsulation encoded with encoding '{encoding}'");
        }

        /// <summary>Determines if a tagged parameter or data member is available.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="expectedFormat">The expected format of the tagged parameter.</param>
        /// <returns>True if the tagged parameter is present; otherwise, false.</returns>
        private bool DecodeTaggedParamHeader(int tag, TagFormat expectedFormat)
        {
            Debug.Assert(Encoding == IceRpc.Encoding.Slice11);

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

                int v = DecodeByte();
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

                    // When expected format is VInt, format can be any of F1 through F8. Note that the exact format
                    // received does not matter in this case.

                    if (format != expectedFormat &&
                        (expectedFormat != TagFormat.VInt || (int)format > (int)TagFormat.F8))
                    {
                        throw new InvalidDataException($"invalid tagged parameter '{tag}': unexpected format");
                    }
                    return true;
                }
            }
        }

        private byte PeekByte() => _reader.TryPeek(out byte value) ? value : throw new EndOfBufferException();

        /// <summary>Skips the remaining tagged parameters, return value _or_ data members.</summary>
        private void SkipTaggedParams()
        {
            if (Encoding == IceRpc.Encoding.Slice11)
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

                    int v = DecodeByte();
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
                // TODO: the current version is for paramaters, return values and exception data members. It relies on
                // the end of buffer to detect the end of the tag "dictionary", and does not use TagEndMarker.
                while (!_reader.End)
                {
                    // Skip tag
                    _ = DecodeVarInt();

                    // Skip tagged value
                    Skip(DecodeSize());
                }
            }
        }

        private void SkipTaggedValue(TagFormat format)
        {
            Debug.Assert(Encoding == IceRpc.Encoding.Slice11);

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
        private class EndOfBufferException : InvalidOperationException
        {
            internal EndOfBufferException()
                : base("attempting to decode past the end of the decoder buffer")
            {
            }
        }
    }
}
