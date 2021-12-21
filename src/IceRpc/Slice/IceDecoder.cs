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

using static IceRpc.Slice.Internal.Ice11Definitions;

namespace IceRpc.Slice
{
    /// <summary>Decodes a byte buffer encoded using the Ice encoding.</summary>
    public ref partial struct IceDecoder
    {
        /// <summary>The Slice encoding decoded by this decoder.</summary>
        public IceEncoding Encoding { get; }

        /// <summary>The 0-based position (index) in the underlying buffer.</summary>
        internal int Pos { get; private set; }

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

        private static readonly System.Text.UTF8Encoding _utf8 = new(false, true);

        private readonly IActivator? _activator;

        // The byte buffer we are decoding.
        private readonly ReadOnlySpan<byte> _buffer;

        private ClassContext _classContext;

        // Connection used when decoding proxies
        private readonly Connection? _connection;

        // Invoker used when decoding proxies.
        private readonly IInvoker? _invoker;

        // The sum of all the minimum sizes (in bytes) of the sequences decoded from this buffer. Must not exceed the
        // buffer size.
        private int _minTotalSeqSize;

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
        {
            Encoding = encoding;

            _activator = activator;
            _classContext = new ClassContext(classGraphMaxDepth);

            _connection = connection;
            _invoker = invoker;
            Pos = 0;
            _buffer = buffer.Span;

            _minTotalSeqSize = 0;
        }

        /// <summary>Constructs a new Ice decoder over a byte buffer.</summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="encoding">The Slice encoding version.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="activator">The activator.</param>
        /// <param name="classGraphMaxDepth">The class graph max depth.</param>
        public IceDecoder(
            ReadOnlySequence<byte> buffer,
            IceEncoding encoding,
            Connection? connection = null,
            IInvoker? invoker = null,
            IActivator? activator = null,
            int classGraphMaxDepth = -1)
            : this(buffer.ToSingleBuffer(), encoding, connection, invoker, activator, classGraphMaxDepth)
        {
        }

        // Decode methods for basic types

        /// <summary>Decodes a bool.</summary>
        /// <returns>The bool decoded by this decoder.</returns>
        public bool DecodeBool() => _buffer[Pos++] == 1;

        /// <summary>Decodes a byte.</summary>
        /// <returns>The byte decoded by this decoder.</returns>
        public byte DecodeByte() => _buffer[Pos++];

        /// <summary>Decodes a double.</summary>
        /// <returns>The double decoded by this decoder.</returns>
        public double DecodeDouble()
        {
            double value = BitConverter.ToDouble(_buffer.Slice(Pos, sizeof(double)));
            Pos += sizeof(double);
            return value;
        }

        /// <summary>Decodes a size encoded on a fixed number of bytes.</summary>
        /// <returns>The size decoded by this decoder.</returns>
        public int DecodeFixedLengthSize()
        {
            if (Encoding == IceRpc.Encoding.Ice11)
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

        /// <summary>Decodes a float.</summary>
        /// <returns>The float decoded by this decoder.</returns>
        public float DecodeFloat()
        {
            float value = BitConverter.ToSingle(_buffer.Slice(Pos, sizeof(float)));
            Pos += sizeof(float);
            return value;
        }

        /// <summary>Decodes an int.</summary>
        /// <returns>The int decoded by this decoder.</returns>
        public int DecodeInt()
        {
            int value = BitConverter.ToInt32(_buffer.Slice(Pos, sizeof(int)));
            Pos += sizeof(int);
            return value;
        }

        /// <summary>Decodes a long.</summary>
        /// <returns>The long decoded by this decoder.</returns>
        public long DecodeLong()
        {
            long value = BitConverter.ToInt64(_buffer.Slice(Pos, sizeof(long)));
            Pos += sizeof(long);
            return value;
        }

        /// <summary>Decodes a short.</summary>
        /// <returns>The short decoded by this decoder.</returns>
        public short DecodeShort()
        {
            short value = BitConverter.ToInt16(_buffer.Slice(Pos, sizeof(short)));
            Pos += sizeof(short);
            return value;
        }

        /// <summary>Decodes a size encoded on a variable number of bytes.</summary>
        /// <returns>The size decoded by this decoder.</returns>
        public int DecodeSize()
        {
            if (Encoding == IceRpc.Encoding.Ice11)
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
                string value = DecodeString(_buffer.Slice(Pos, size));
                Pos += size;
                return value;
            }

            static string DecodeString(ReadOnlySpan<byte> from) => from.IsEmpty ? "" : _utf8.GetString(from);
        }

        /// <summary>Decodes a uint.</summary>
        /// <returns>The uint decoded by this decoder.</returns>
        public uint DecodeUInt()
        {
            uint value = BitConverter.ToUInt32(_buffer.Slice(Pos, sizeof(uint)));
            Pos += sizeof(uint);
            return value;
        }

        /// <summary>Decodes a ulong.</summary>
        /// <returns>The ulong decoded by this decoder.</returns>
        public ulong DecodeULong()
        {
            ulong value = BitConverter.ToUInt64(_buffer.Slice(Pos, sizeof(ulong)));
            Pos += sizeof(ulong);
            return value;
        }

        /// <summary>Decodes a ushort.</summary>
        /// <returns>The ushort decoded by this decoder.</returns>
        public ushort DecodeUShort()
        {
            ushort value = BitConverter.ToUInt16(_buffer.Slice(Pos, sizeof(ushort)));
            Pos += sizeof(ushort);
            return value;
        }

        /// <summary>Decodes an int. This int is encoded using Ice's variable-size integer encoding.
        /// </summary>
        /// <returns>The int decoded by this decoder.</returns>
        public int DecodeVarInt()
        {
            try
            {
                checked
                {
                    return (int)DecodeVarLong();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("varint value is out of range", ex);
            }
        }

        /// <summary>Decodes a long. This long is encoded using Ice's variable-size integer encoding.
        /// </summary>
        /// <returns>The long decoded by this decoder.</returns>
        public long DecodeVarLong() =>
            (_buffer[Pos] & 0x03) switch
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
                checked
                {
                    return (uint)DecodeVarULong();
                }
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("varuint value is out of range", ex);
            }
        }

        /// <summary>Decodes a ulong. This ulong is encoded using Ice's variable-size integer encoding.
        /// </summary>
        /// <returns>The ulong decoded by this decoder.</returns>
        public ulong DecodeVarULong() =>
            (_buffer[Pos] & 0x03) switch
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
            if (Encoding == IceRpc.Encoding.Ice11)
            {
                return DecodeExceptionClass();
            }
            else
            {
                string typeId = DecodeString();
                var remoteEx = _activator?.CreateInstance(typeId, ref this) as RemoteException;

                if (remoteEx == null)
                {
                    // If we can't decode this exception, we return an UnknownSlicedRemoteException instead of throwing
                    // "can't decode remote exception".
                    return new UnknownSlicedRemoteException(typeId, ref this);
                }
                else
                {
                    // TODO: consider calling this Skip for the remaining exception tagged members from the generated
                    // code to make the exception decoding constructor usable directly. See protocol bridging code.
                    SkipTaggedParams();
                    return remoteEx;
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

            if (Encoding == IceRpc.Encoding.Ice11)
            {
                var identity = new Identity(ref this);
                if (identity.Name.Length == 0) // such identity means received a null proxy with the 1.1 encoding
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
                var protocol = Protocol.FromProtocolCode((ProtocolCode)proxyData.ProtocolMajor);
                if (size == 0)
                {
                    string adapterId = DecodeString();
                    if (adapterId.Length > 0)
                    {
                        if (protocol == Protocol.Ice1)
                        {
                            endpoint = new Endpoint(Protocol.Ice1,
                                                    TransportNames.Loc,
                                                    host: adapterId,
                                                    port: Ice1Parser.DefaultPort,
                                                    @params: ImmutableList<EndpointParam>.Empty);
                        }
                        else
                        {
                            throw new InvalidDataException($"received {protocol} proxy with an adapter ID");
                        }
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

                if (protocol == Protocol.Ice1)
                {
                    if (proxyData.OptionalFacet.Count > 1)
                    {
                        throw new InvalidDataException(
                            $"received proxy with {proxyData.OptionalFacet.Count} elements in its optionalFacet");
                    }

                    try
                    {
                        var identityAndFacet = new IdentityAndFacet(identity, proxyData.OptionalFacet);
                        var proxy = new Proxy(identityAndFacet.ToPath(), Protocol.Ice1);
                        proxy.Encoding = IceRpc.Encoding.FromMajorMinor(
                            proxyData.EncodingMajor,
                            proxyData.EncodingMinor);
                        proxy.Endpoint = endpoint;
                        proxy.AltEndpoints = altEndpoints.ToImmutableList();
                        proxy.Invoker = _invoker;
                        return proxy;
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
                    if (proxyData.OptionalFacet.Count > 0)
                    {
                        throw new InvalidDataException($"received proxy for protocol {protocol} with a facet");
                    }
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
                            proxy = new Proxy(identity.ToPath(), protocol);
                            proxy.Endpoint = endpoint;
                            proxy.AltEndpoints = altEndpoints.ToImmutableList();
                            proxy.Invoker = _invoker;
                        }

                        proxy.Encoding = IceRpc.Encoding.FromMajorMinor(proxyData.EncodingMajor,
                            proxyData.EncodingMinor);
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

                Protocol protocol = proxyData.Protocol != null ?
                    Protocol.FromProtocolCode(proxyData.Protocol.Value) :
                    Protocol.Ice2;
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

                    if (endpoint == null && protocol != Protocol.Ice1)
                    {
                        proxy = Proxy.FromConnection(_connection!, proxyData.Path, _invoker);
                    }
                    else
                    {
                        proxy = new Proxy(proxyData.Path, protocol);
                        proxy.Endpoint = endpoint;
                        proxy.AltEndpoints = altEndpoints;
                        proxy.Invoker = _invoker;
                    }

                    proxy.Encoding = proxyData.Encoding is string encoding ?
                        IceRpc.Encoding.FromString(encoding) : (proxy.Protocol.IceEncoding ?? IceRpc.Encoding.Unknown);

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
            int byteCount = elementSize * value.Length;
            _buffer.Slice(Pos, byteCount).CopyTo(MemoryMarshal.Cast<T, byte>(value));
            Pos += byteCount;

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

        /// <summary>Decodes a bit sequence.</summary>
        /// <param name="bitSequenceSize">The minimum number of bits in the sequence.</param>
        /// <returns>The read-only bit sequence decoded by this decoder.</returns>
        public ReadOnlyBitSequence DecodeBitSequence(int bitSequenceSize)
        {
            int size = (bitSequenceSize >> 3) + ((bitSequenceSize & 0x07) != 0 ? 1 : 0);
            int startPos = Pos;
            Pos += size;
            return new ReadOnlyBitSequence(_buffer.Slice(startPos, size));
        }

        /// <summary>Decodes fields.</summary>
        /// <returns>The fields as an immutable dictionary.</returns>
        public ImmutableDictionary<int, ReadOnlyMemory<byte>> DecodeFieldDictionary()
        {
            int size = DecodeSize();
            if (size == 0)
            {
                return ImmutableDictionary<int, ReadOnlyMemory<byte>>.Empty;
            }
            else
            {
                var builder = ImmutableDictionary.CreateBuilder<int, ReadOnlyMemory<byte>>();
                for (int i = 0; i < size; ++i)
                {
                    (int key, ReadOnlyMemory<byte> value) = DecodeField();
                    builder.Add(key, value);
                }
                return builder.ToImmutable();
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
            if (Encoding == IceRpc.Encoding.Ice11)
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

                while (true)
                {
                    if (_buffer.Length - Pos <= 0)
                    {
                        return default!; // End of buffer indicates end of tagged parameters.
                    }

                    int savedPos = Pos;
                    tag = DecodeVarInt();

                    if (tag == requestedTag)
                    {
                        // Found requested tag, so skip size:
                        Skip(IceEncoding.DecodeVarLongLength(_buffer[Pos]));
                        return decodeFunc(ref this);
                    }
                    else if (tag > requestedTag)
                    {
                        Pos = savedPos; // rewind
                        return default!;
                    }
                    else
                    {
                        Skip(DecodeSize());
                        // and continue while loop
                    }
                }
            }
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

            if (Pos != _buffer.Length)
            {
                throw new InvalidDataException($"{_buffer.Length - Pos} bytes remaining in the buffer");
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
            int sz = DecodeSize();

            if (sz == 0)
            {
                return 0;
            }

            // When minElementSize is 0, we only count of bytes that hold the bit sequence.
            int minSize = minElementSize > 0 ? sz * minElementSize : (sz >> 3) + ((sz & 0x07) != 0 ? 1 : 0);

            // With _minTotalSeqSize, we make sure that multiple sequences within a buffer can't trigger maliciously
            // the allocation of a large amount of memory before we decode these sequences.
            _minTotalSeqSize += minSize;

            if (Pos + minSize > _buffer.Length || _minTotalSeqSize > _buffer.Length)
            {
                throw new InvalidDataException("invalid sequence size");
            }
            return sz;
        }

        internal ReadOnlySpan<byte> DecodeBitSequenceSpan(int bitSequenceSize)
        {
            int size = (bitSequenceSize >> 3) + ((bitSequenceSize & 0x07) != 0 ? 1 : 0);
            int startPos = Pos;
            Pos += size;
            return _buffer.Slice(startPos, size);
        }

        /// <summary>Decodes a field.</summary>
        /// <returns>The key and value of the field.</returns>
        internal (int Key, ReadOnlyMemory<byte> Value) DecodeField()
        {
            int key = DecodeVarInt();
            int entrySize = DecodeSize();
            byte[] value = _buffer.Slice(Pos, entrySize).ToArray(); // make a copy
            Pos += entrySize;
            return (key, value);
        }

        /// <summary>Reads size bytes from the underlying buffer.</summary>
        internal ReadOnlySpan<byte> ReadBytes(int size)
        {
            Debug.Assert(size > 0);
            var result = _buffer.Slice(Pos, size);
            Pos += size;
            return result;
        }

        internal void Skip(int size)
        {
            if (size < 0 || size > _buffer.Length - Pos)
            {
                throw new IndexOutOfRangeException($"cannot skip {size} bytes");
            }
            Pos += size;
        }

        /// <summary>Decodes an endpoint (Slice 1.1).</summary>
        /// <param name="protocol">The Ice protocol of this endpoint.</param>
        /// <returns>The endpoint decoded by this decoder.</returns>
        private Endpoint DecodeEndpoint(Protocol protocol)
        {
            Debug.Assert(Encoding == IceRpc.Encoding.Ice11);

            // The Ice 1.1 encoding of ice1 endpoints is transport-specific, and hard-coded here and in the
            // Ice11Encoder. The preferred and fallback encoding for new transports is TransportCode.Any, which uses an
            // EndpointData like Ice 2.0.

            Debug.Assert(_connection != null);

            Endpoint? endpoint = null;
            TransportCode transportCode = this.DecodeTransportCode();

            int size = DecodeInt();
            if (size < 6)
            {
                throw new InvalidDataException($"the 1.1 encapsulation's size ({size}) is too small");
            }

            if (size - 4 > _buffer.Length - Pos)
            {
                throw new InvalidDataException(
                    $"the encapsulation's size ({size}) extends beyond the end of the buffer");
            }

            // Remove 6 bytes from the encapsulation size (4 for encapsulation size, 2 for encoding).
            size -= 6;

            var encoding = IceRpc.Encoding.FromMajorMinor(DecodeByte(), DecodeByte());

            if (encoding == IceRpc.Encoding.Ice11 || encoding == IceRpc.Encoding.Ice10)
            {
                int oldPos = Pos;

                if (protocol == Protocol.Ice1)
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

                            var endpointParams = ImmutableList<EndpointParam>.Empty;

                            if (timeout != EndpointParseExtensions.DefaultTcpTimeout)
                            {
                                endpointParams = endpointParams.Add(
                                    new EndpointParam("-t", timeout.ToString(CultureInfo.InvariantCulture)));
                            }
                            if (compress)
                            {
                                endpointParams = endpointParams.Add(new EndpointParam("-z", ""));
                            }

                            endpoint = new Endpoint(Protocol.Ice1,
                                                    transportCode == TransportCode.SSL ?
                                                        TransportNames.Ssl : TransportNames.Tcp,
                                                    host,
                                                    port,
                                                    endpointParams);

                            break;
                        }

                        case TransportCode.UDP:
                        {
                            string host = DecodeString();
                            ushort port = checked((ushort)DecodeInt());
                            bool compress = DecodeBool();

                            var endpointParams = compress ? ImmutableList.Create(new EndpointParam("-z", "")) :
                                ImmutableList<EndpointParam>.Empty;

                            endpoint = new Endpoint(Protocol.Ice1,
                                                    TransportNames.Udp,
                                                    host,
                                                    port,
                                                    endpointParams);

                            // else endpoint remains null and we throw below
                            break;
                        }

                        case TransportCode.Any:
                            endpoint = new EndpointData(ref this).ToEndpoint();
                            break;

                        default:
                        {
                            // Create an endpoint for transport opaque
                            var endpointParams = ImmutableList.Create(
                                    new EndpointParam("-t",
                                                      ((short)transportCode).ToString(CultureInfo.InvariantCulture)),
                                    new EndpointParam("-e", encoding.ToString()),
                                    new EndpointParam("-v", Convert.ToBase64String(_buffer.Slice(Pos, size))));

                            Pos += size;

                            endpoint = new Endpoint(Protocol.Ice1,
                                                    TransportNames.Opaque,
                                                    host: "",
                                                    port: 0,
                                                    endpointParams);
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
                    if (Pos != oldPos + size)
                    {
                        throw new InvalidDataException($"{oldPos + size - Pos} bytes left in endpoint encapsulation");
                    }
                }
            }

            string transportName = endpoint?.Transport ?? transportCode.ToString().ToLowerInvariant();

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
            Debug.Assert(Encoding == IceRpc.Encoding.Ice11);

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
                if (!withTagEndMarker && _buffer.Length - Pos <= 0)
                {
                    return false; // End of buffer indicates end of tagged parameters.
                }

                int savedPos = Pos;

                int v = DecodeByte();
                if (withTagEndMarker && v == TagEndMarker)
                {
                    Pos = savedPos; // rewind
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
                    Pos = savedPos; // rewind
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

        /// <summary>Skips the remaining tagged parameters, return value _or_ data members.</summary>
        private void SkipTaggedParams()
        {
            if (Encoding == IceRpc.Encoding.Ice11)
            {
                bool withTagEndMarker = (_classContext.Current.InstanceType != InstanceType.None);

                while (true)
                {
                    if (!withTagEndMarker && _buffer.Length - Pos <= 0)
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
                while (true)
                {
                    if (_buffer.Length - Pos <= 0)
                    {
                        break; // end of buffer, done
                    }

                    // Skip tag
                    _ = DecodeVarInt();

                    // Skip tagged value
                    Skip(DecodeSize());
                }
            }
        }

        private int ReadSpan(Span<byte> span)
        {
            int length = Math.Min(span.Length, _buffer.Length - Pos);
            _buffer.Slice(Pos, length).CopyTo(span);
            Pos += length;
            return length;
        }

        private void SkipSize()
        {
            Debug.Assert(Encoding == IceRpc.Encoding.Ice11);

            byte b = DecodeByte();
            if (b == 255)
            {
                Skip(4);
            }
        }

        private void SkipTaggedValue(TagFormat format)
        {
            Debug.Assert(Encoding == IceRpc.Encoding.Ice11);

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
                    Skip(DecodeFixedLengthSize());
                    break;
                default:
                    throw new InvalidDataException(
                        $"cannot skip tagged parameter or data member with tag format '{format}'");
            }
        }
    }
}
