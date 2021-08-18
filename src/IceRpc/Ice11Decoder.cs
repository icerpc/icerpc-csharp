// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports.Internal;
using System.Collections;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc
{
    /// <summary>Decoder for the Ice 1.1 encoding.</summary>
    public class Ice11Decoder : IceDecoder
    {
        /// <summary>The sliced-off slices held by the current instance, if any.</summary>
        internal SlicedData? SlicedData
        {
            get
            {
                Debug.Assert(_current.InstanceType != InstanceType.None);
                if (_current.Slices == null)
                {
                    return null;
                }
                else
                {
                    return new SlicedData(Encoding.Ice11, _current.Slices);
                }
            }
        }

        private readonly IClassFactory _classFactory;

        private readonly int _classGraphMaxDepth;

        // Data for the class or exception instance that is currently getting decoded.
        private InstanceData _current;

        // The current depth when decoding nested class instances.
        private int _classGraphDepth;

        // Map of class instance ID to class instance.
        // When decoding a buffer:
        //  - Instance ID = 0 means null
        //  - Instance ID = 1 means the instance is encoded inline afterwards
        //  - Instance ID > 1 means a reference to a previously decoded instance, found in this map.
        // Since the map is actually a list, we use instance ID - 2 to lookup an instance.
        private List<AnyClass>? _instanceMap;

          // See DecodeTypeId.
        private int _posAfterLatestInsertedTypeId;

        // Map of type ID index to type ID sequence, used only for classes.
        // We assign a type ID index (starting with 1) to each type ID (type ID sequence) we decode, in order.
        // Since this map is a list, we lookup a previously assigned type ID (type ID sequence) with
        // _typeIdMap[index - 1].
        private List<string>? _typeIdMap;

        public override RemoteException DecodeException()
        {
            Debug.Assert(_current.InstanceType == InstanceType.None);
            _current.InstanceType = InstanceType.Exception;

            RemoteException? remoteEx = null;
            string? errorMessage = null;
            RemoteExceptionOrigin origin = RemoteExceptionOrigin.Unknown;

            // We can decode the indirection table (if there is one) immediately after decoding each slice header
            // because the indirection table cannot reference the exception itself.
            // Each slice contains its type ID as a string.

            do
            {
                // The type ID is always decoded for an exception and cannot be null.
                string? typeId = DecodeSliceHeaderIntoCurrent();
                Debug.Assert(typeId != null);

                DecodeIndirectionTableIntoCurrent(); // we decode the indirection table immediately.

                remoteEx = _classFactory.CreateRemoteException(typeId, errorMessage, origin);
                if (remoteEx == null && SkipSlice(typeId)) // Slice off what we don't understand.
                {
                    break;
                }
            }
            while (remoteEx == null);

            remoteEx ??= new RemoteException(errorMessage, origin);
            remoteEx.SlicedData = SlicedData;

            _current.FirstSlice = true;
            remoteEx.Decode(this);
            _current = default;
            return remoteEx;
        }

        public override T? DecodeNullableClass<T>() where T : class
        {
            AnyClass? obj = DecodeAnyClass();
            if (obj is T result)
            {
                return result;
            }
            else if (obj == null)
            {
                return null;
            }
            else
            {
                throw new InvalidDataException(@$"decoded instance of type '{obj.GetType().FullName
                    }' but expected instance of type '{typeof(T).FullName}'");
            }
        }

        public override Proxy? DecodeNullableProxy()
        {
            Debug.Assert(Connection != null);

            var identity = new Identity(this);
            if (identity.Name.Length == 0) // such identity means received a null proxy with the 1.1 encoding
            {
                return null;
            }

            var proxyData = new ProxyData11(this);

            if ((byte)proxyData.Protocol == 0)
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

            if (size == 0)
            {
                string adapterId = DecodeString();
                if (adapterId.Length > 0)
                {
                    if (proxyData.Protocol == Protocol.Ice1)
                    {
                        endpoint = new Endpoint(Protocol.Ice1,
                                                TransportNames.Loc,
                                                Host: adapterId,
                                                Port: Ice1Parser.DefaultPort,
                                                Params: ImmutableList<EndpointParam>.Empty);
                    }
                    else
                    {
                        throw new InvalidDataException(
                            $"received {proxyData.Protocol.GetName()} proxy with an adapter ID");
                    }
                }
            }
            else
            {
                endpoint = DecodeEndpoint(proxyData.Protocol);
                if (size >= 2)
                {
                    var endpointArray = new Endpoint[size - 1];
                    for (int i = 0; i < size - 1; ++i)
                    {
                        endpointArray[i] = DecodeEndpoint(proxyData.Protocol);
                    }
                    altEndpoints = endpointArray;
                }
            }

            if (proxyData.Protocol == Protocol.Ice1)
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
                    proxy.Encoding = Encoding.FromMajorMinor(proxyData.EncodingMajor, proxyData.EncodingMinor);
                    proxy.Endpoint = endpoint;
                    proxy.AltEndpoints = altEndpoints.ToImmutableList();
                    proxy.Invoker = Invoker;
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
                    throw new InvalidDataException(
                        $"received proxy for protocol {proxyData.Protocol.GetName()} with a facet");
                }
                if (proxyData.InvocationMode != InvocationMode.Twoway)
                {
                    throw new InvalidDataException(
                        $"received proxy for protocol {proxyData.Protocol.GetName()} with invocation mode set");
                }

                try
                {
                    Proxy proxy;

                    if (endpoint == null)
                    {
                        proxy = Proxy.FromConnection(Connection, identity.ToPath(), Invoker);
                    }
                    else
                    {
                        proxy = new Proxy(identity.ToPath(), proxyData.Protocol);
                        proxy.Endpoint = endpoint;
                        proxy.AltEndpoints = altEndpoints.ToImmutableList();
                        proxy.Invoker = Invoker;
                    }

                    proxy.Encoding = Encoding.FromMajorMinor(proxyData.EncodingMajor, proxyData.EncodingMinor);
                    return proxy;
                }
                catch (Exception ex)
                {
                    throw new InvalidDataException("received invalid proxy", ex);
                }
            }
        }

        public override int DecodeSize()
        {
            byte b = DecodeByte();
            if (b < 255)
            {
                return b;
            }

            int size = DecodeInt();
            if (size < 0)
            {
                throw new InvalidDataException($"decoded invalid size: {size}");
            }
            return size;
        }

        /// <inheritdoc/>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void IceEndDerivedExceptionSlice() => IceEndException();

        /// <inheritdoc/>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void IceEndException() => IceEndSlice();

        /// <summary>Tells the decoder the end of a class was reached.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void IceEndSlice()
        {
            // Note that IceEndSlice is not called when we call SkipSlice.
            Debug.Assert(_current.InstanceType != InstanceType.None);

            if ((_current.SliceFlags & EncodingDefinitions.SliceFlags.HasTaggedMembers) != 0)
            {
                SkipTaggedParams();
            }
            if ((_current.SliceFlags & EncodingDefinitions.SliceFlags.HasIndirectionTable) != 0)
            {
                Debug.Assert(_current.PosAfterIndirectionTable != null && _current.IndirectionTable != null);
                Pos = _current.PosAfterIndirectionTable.Value;
                _current.PosAfterIndirectionTable = null;
                _current.IndirectionTable = null;
            }
        }

        /// <inheritdoc/>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void IceStartDerivedExceptionSlice() => IceStartException();

        /// <inheritdoc/>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public override void IceStartException()
        {
            Debug.Assert(_current.InstanceType != InstanceType.None);
            if (_current.FirstSlice)
            {
                _current.FirstSlice = false;
            }
            else
            {
                IceStartNextSlice();
            }
        }

        /// <summary>Starts decoding the first slice of a class.</summary>
        /// <returns>The sliced-off slices, if any.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public SlicedData? IceStartFirstSlice() => SlicedData;

        /// <summary>Starts decoding a base slice of a class instance (any slice except the first slice).</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void IceStartNextSlice()
        {
            DecodeNextSliceHeaderIntoCurrent();
            DecodeIndirectionTableIntoCurrent();
        }

        /// <summary>Constructs a new decoder for the Ice 1.1 encoding.</summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="classFactory">The class factory, used to decode classes and exceptions.</param>
        internal Ice11Decoder(
            ReadOnlyMemory<byte> buffer,
            Connection? connection = null,
            IInvoker? invoker = null,
            IClassFactory? classFactory = null)
            : base(buffer, connection, invoker)
        {
            _classFactory = classFactory ?? ClassFactory.Default;
            _classGraphMaxDepth = connection?.ClassGraphMaxDepth ?? 100;
        }

        private protected override bool DecodeTaggedParamHeader(int tag, EncodingDefinitions.TagFormat expectedFormat)
        {
            // The current slice has no tagged parameter.
            if (_current.InstanceType != InstanceType.None &&
                (_current.SliceFlags & EncodingDefinitions.SliceFlags.HasTaggedMembers) == 0)
            {
                return false;
            }

            return base.DecodeTaggedParamHeader(tag, expectedFormat);
        }

        private protected override void SkipFixedLengthSize() => Skip(4);

        private protected override void SkipSize()
        {
            byte b = DecodeByte();
            if (b == 255)
            {
                Skip(4);
            }
        }

        private protected override void SkipTagged(EncodingDefinitions.TagFormat format)
        {
            switch (format)
            {
                case EncodingDefinitions.TagFormat.F1:
                    Skip(1);
                    break;
                case EncodingDefinitions.TagFormat.F2:
                    Skip(2);
                    break;
                case EncodingDefinitions.TagFormat.F4:
                    Skip(4);
                    break;
                case EncodingDefinitions.TagFormat.F8:
                    Skip(8);
                    break;
                case EncodingDefinitions.TagFormat.Size:
                    SkipSize();
                    break;
                case EncodingDefinitions.TagFormat.VSize:
                    Skip(DecodeSize());
                    break;
                case EncodingDefinitions.TagFormat.FSize:
                    int size = DecodeInt();
                    if (size < 0)
                    {
                        throw new InvalidDataException("invalid negative fixed-length size");
                    }
                    Skip(size);
                    break;
                default:
                    throw new InvalidDataException(
                        $"cannot skip tagged parameter or data member with tag format '{format}'");
            }
        }

        /// <summary>Decodes a class instance.</summary>
        /// <returns>The class instance. Can be null.</returns>
        private AnyClass? DecodeAnyClass()
        {
            int index = DecodeSize();
            if (index < 0)
            {
                throw new InvalidDataException($"invalid index {index} while decoding a class");
            }
            else if (index == 0)
            {
                return null;
            }
            else if (_current.InstanceType != InstanceType.None &&
                (_current.SliceFlags & EncodingDefinitions.SliceFlags.HasIndirectionTable) != 0)
            {
                // When decoding an instance within a slice and there is an indirection table, we have an index within
                // this indirection table.
                // We need to decrement index since position 0 in the indirection table corresponds to index 1.
                index--;
                if (index < _current.IndirectionTable?.Length)
                {
                    return _current.IndirectionTable[index];
                }
                else
                {
                    throw new InvalidDataException("index too big for indirection table");
                }
            }
            else
            {
                return DecodeInstance(index);
            }
        }

        /// <summary>Decodes an endpoint.</summary>
        /// <param name="protocol">The Ice protocol of this endpoint.</param>
        /// <returns>The endpoint decoded by this decoder.</returns>
        private Endpoint DecodeEndpoint(Protocol protocol)
        {
            Debug.Assert(Connection != null);

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

            var encoding = Encoding.FromMajorMinor(DecodeByte(), DecodeByte());

            if (encoding == Encoding.Ice11 || encoding == Encoding.Ice10)
            {
                int oldPos = Pos;

                if (transportCode == TransportCode.Any)
                {
                    // Encoded as an endpoint data
                    endpoint = new EndpointData(this).ToEndpoint();
                }
                else if (protocol == Protocol.Ice1)
                {
                    endpoint = Connection.EndpointCodex.DecodeEndpoint(transportCode, this);

                    if (endpoint == null)
                    {
                        var endpointParams = ImmutableList.Create(
                            new EndpointParam("-t", ((short)transportCode).ToString(CultureInfo.InvariantCulture)),
                            new EndpointParam("-e", encoding.ToString()),
                            new EndpointParam("-v", Convert.ToBase64String(_buffer.Slice(Pos, size).Span)));

                        endpoint = new Endpoint(
                            protocol,
                            TransportNames.Opaque,
                            Host: "",
                            Port: 0,
                            endpointParams);

                        Pos += size;
                    }
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
                    @$"cannot decode endpoint for protocol '{protocol.GetName()}' and transport '{transportName
                    }' with endpoint encapsulation encoded with encoding '{encoding}'");
        }

        /// <summary>Decodes an indirection table without updating _current.</summary>
        /// <returns>The indirection table.</returns>
        private AnyClass[] DecodeIndirectionTable()
        {
            int size = DecodeAndCheckSeqSize(1);
            if (size == 0)
            {
                throw new InvalidDataException("invalid empty indirection table");
            }
            var indirectionTable = new AnyClass[size];
            for (int i = 0; i < indirectionTable.Length; ++i)
            {
                int index = DecodeSize();
                if (index < 1)
                {
                    throw new InvalidDataException($"decoded invalid index {index} in indirection table");
                }
                indirectionTable[i] = DecodeInstance(index);
            }
            return indirectionTable;
        }

        /// <summary>Decodes the indirection table into _current's fields if there is an indirection table.
        /// Precondition: called after decoding the header of the current slice. This method does not change _pos.
        /// </summary>
        private void DecodeIndirectionTableIntoCurrent()
        {
            Debug.Assert(_current.IndirectionTable == null);
            if ((_current.SliceFlags & EncodingDefinitions.SliceFlags.HasIndirectionTable) != 0)
            {
                if ((_current.SliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) == 0)
                {
                    throw new InvalidDataException("slice has indirection table but does not have a size");
                }

                int savedPos = Pos;
                Pos = savedPos + _current.SliceSize;
                _current.IndirectionTable = DecodeIndirectionTable();
                _current.PosAfterIndirectionTable = Pos;
                Pos = savedPos;
            }
        }

        /// <summary>Decodes a class instance.</summary>
        /// <param name="index">The index of the class instance. If greater than 1, it's a reference to a previously
        /// seen class; if 1, the class's bytes are next. Cannot be 0 or less.</param>
        private AnyClass DecodeInstance(int index)
        {
            Debug.Assert(index > 0);

            if (index > 1)
            {
                if (_instanceMap != null && _instanceMap.Count > index - 2)
                {
                    return _instanceMap[index - 2];
                }
                throw new InvalidDataException($"could not find index {index} in {nameof(_instanceMap)}");
            }

            if (++_classGraphDepth > _classGraphMaxDepth)
            {
                throw new InvalidDataException("maximum class graph depth reached");
            }

            // Save current in case we're decoding a nested instance.
            InstanceData previousCurrent = _current;
            _current = default;
            _current.InstanceType = InstanceType.Class;

            AnyClass? instance = null;
            _instanceMap ??= new List<AnyClass>();

            bool decodeIndirectionTable = true;
            do
            {
                // Decode the slice header.
                string? typeId = DecodeSliceHeaderIntoCurrent();

                // We cannot decode the indirection table at this point as it may reference the new instance that is
                // not created yet.
                if (typeId != null)
                {
                    instance = _classFactory.CreateClassInstance(typeId);
                }

                if (instance == null && SkipSlice(typeId)) // Slice off what we don't understand.
                {
                    instance = new UnknownSlicedClass();
                    // Don't decode the indirection table as it's the last entry in DeferredIndirectionTableList.
                    decodeIndirectionTable = false;
                }
            }
            while (instance == null);

            // Add the instance to the map/list of instances. This must be done before decoding the instances (for
            // circular references).
            _instanceMap.Add(instance);

            // Decode all the deferred indirection tables now that the instance is inserted in _instanceMap.
            if (_current.DeferredIndirectionTableList?.Count > 0)
            {
                int savedPos = Pos;

                Debug.Assert(_current.Slices?.Count == _current.DeferredIndirectionTableList.Count);
                for (int i = 0; i < _current.DeferredIndirectionTableList.Count; ++i)
                {
                    int pos = _current.DeferredIndirectionTableList[i];
                    if (pos > 0)
                    {
                        Pos = pos;
                        _current.Slices[i].Instances = Array.AsReadOnly(DecodeIndirectionTable());
                    }
                    // else remains empty
                }

                Pos = savedPos;
            }

            if (decodeIndirectionTable)
            {
                DecodeIndirectionTableIntoCurrent();
            }

            instance.Decode(this);

            _current = previousCurrent;
            --_classGraphDepth;
            return instance;
        }

        /// <summary>Decodes the header of the current slice into _current; this method is used when the current slice
        /// is not the first (most derived) slice.</summary>
        private void DecodeNextSliceHeaderIntoCurrent()
        {
            // With the 1.1 encoding, each slice header in sliced format contains a type ID - we decode it and
            // ignore it.
            _ = DecodeSliceHeaderIntoCurrent();
        }

        /// <summary>Decodes the header of the current slice into _current.</summary>
        /// <returns>The type ID or the compact ID of the current slice.</returns>
        private string? DecodeSliceHeaderIntoCurrent()
        {
            _current.SliceFlags = (EncodingDefinitions.SliceFlags)DecodeByte();

            string? typeId;
            // Decode the type ID. For class slices, the type ID is encoded as a string or as an index or as a compact
            // ID, for exceptions it's always encoded as a string.
            if (_current.InstanceType == InstanceType.Class)
            {
                typeId = DecodeTypeId(_current.SliceFlags.GetTypeIdKind());

                if (typeId == null)
                {
                    if ((_current.SliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) != 0)
                    {
                        // A slice in compact format cannot carry a size.
                        throw new InvalidDataException("inconsistent slice flags");
                    }
                }
            }
            else
            {
                // Exception slices always include the type ID, even when using the compact format.
                typeId = DecodeString();
            }

            // Decode the slice size if available.
            if ((_current.SliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) != 0)
            {
                _current.SliceSize = DecodeSliceSize();
            }
            else
            {
                _current.SliceSize = 0;
            }

            // Clear other per-slice fields:
            _current.IndirectionTable = null;
            _current.PosAfterIndirectionTable = null;

            return typeId;
        }

        /// <summary>Decodes the size of the current slice.</summary>
        /// <returns>The slice of the current slice, not including the size length.</returns>
        private int DecodeSliceSize()
        {
            int size = DecodeInt();
            if (size < 4)
            {
                throw new InvalidDataException($"invalid slice size: {size}");
            }
            // With the 1.1 encoding, the encoded size includes the size length.
            return size - 4;
        }

        /// <summary>Decodes the type ID of a class instance.</summary>
        /// <param name="typeIdKind">The kind of type ID to decode.</param>
        /// <returns>The type ID or the compact ID, if any.</returns>
        private string? DecodeTypeId(EncodingDefinitions.TypeIdKind typeIdKind)
        {
            _typeIdMap ??= new List<string>();

            switch (typeIdKind)
            {
                case EncodingDefinitions.TypeIdKind.Index:
                    int index = DecodeSize();
                    if (index > 0 && index - 1 < _typeIdMap.Count)
                    {
                        // The encoded type-id indexes start at 1, not 0.
                        return _typeIdMap[index - 1];
                    }
                    throw new InvalidDataException($"decoded invalid type ID index {index}");

                case EncodingDefinitions.TypeIdKind.String:
                    string typeId = DecodeString();

                    // The typeIds of slices in indirection tables can be decoded several times: when we skip the
                    // indirection table and later on when we decode it. We only want to add this typeId to the list and
                    // assign it an index when it's the first time we decode it, so we save the largest position we
                    // decode to figure out when to add to the list.
                    if (Pos > _posAfterLatestInsertedTypeId)
                    {
                        _posAfterLatestInsertedTypeId = Pos;
                        _typeIdMap.Add(typeId);
                    }
                    return typeId;

                case EncodingDefinitions.TypeIdKind.CompactId:
                    return DecodeSize().ToString(CultureInfo.InvariantCulture);

                default:
                    // TypeIdKind has only 4 possible values.
                    Debug.Assert(typeIdKind == EncodingDefinitions.TypeIdKind.None);
                    return null;
            }
        }

        /// <summary>Skips the indirection table. The caller must save the current position before calling
        /// SkipIndirectionTable (to decode the indirection table at a later point) except when the caller is
        /// SkipIndirectionTable itself.</summary>
        private void SkipIndirectionTable()
        {
            // We should never skip an exception's indirection table
            Debug.Assert(_current.InstanceType == InstanceType.Class);

            // We use DecodeSize and not DecodeAndCheckSeqSize here because we don't allocate memory for this sequence,
            // and since we are skipping this sequence to decode it later, we don't want to double-count its
            // contribution to _minTotalSeqSize.
            int tableSize = DecodeSize();
            for (int i = 0; i < tableSize; ++i)
            {
                int index = DecodeSize();
                if (index <= 0)
                {
                    throw new InvalidDataException($"decoded invalid index {index} in indirection table");
                }
                if (index == 1)
                {
                    if (++_classGraphDepth > _classGraphMaxDepth)
                    {
                        throw new InvalidDataException("maximum class graph depth reached");
                    }

                    // Decode/skip this instance
                    EncodingDefinitions.SliceFlags sliceFlags;
                    do
                    {
                        sliceFlags = (EncodingDefinitions.SliceFlags)DecodeByte();

                        // Skip type ID - can update _typeIdMap
                        _ = DecodeTypeId(sliceFlags.GetTypeIdKind());

                        // Decode the slice size, then skip the slice
                        if ((sliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) == 0)
                        {
                            throw new InvalidDataException("size of slice missing");
                        }
                        int sliceSize = DecodeSliceSize();
                        Pos += sliceSize; // we need a temporary sliceSize because DecodeSliceSize updates _pos.

                        // If this slice has an indirection table, skip it too.
                        if ((sliceFlags & EncodingDefinitions.SliceFlags.HasIndirectionTable) != 0)
                        {
                            SkipIndirectionTable();
                        }
                    }
                    while ((sliceFlags & EncodingDefinitions.SliceFlags.IsLastSlice) == 0);
                    _classGraphDepth--;
                }
            }
        }

        /// <summary>Skips and saves the body of the current slice; also skips and save the indirection table (if any).
        /// </summary>
        /// <param name="typeId">The type ID or compact ID of the current slice.</param>
        /// <returns>True when the current slice is the last slice; otherwise, false.</returns>
        private bool SkipSlice(string? typeId)
        {
            if (typeId == null)
            {
                throw new InvalidDataException("cannot skip a class slice with no type ID");
            }

            if ((_current.SliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) == 0)
            {
                string kind = _current.InstanceType.ToString().ToLowerInvariant();
                throw new InvalidDataException(@$"no {kind} found for type ID '{typeId
                        }' and compact format prevents slicing (the sender should use the sliced format instead)");
            }

            bool hasTaggedMembers = (_current.SliceFlags & EncodingDefinitions.SliceFlags.HasTaggedMembers) != 0;
            byte[] bytes;
            int bytesCopied;
            if (hasTaggedMembers)
            {
                // Don't include the tag end marker. It will be re-written by IceEndSlice when the sliced data is
                // re-written.
                bytes = new byte[_current.SliceSize - 1];
                bytesCopied = ReadSpan(bytes);
                Skip(1);
            }
            else
            {
                bytes = new byte[_current.SliceSize];
                bytesCopied = ReadSpan(bytes);
            }

            if (bytesCopied != bytes.Length)
            {
                throw new InvalidDataException("the slice size extends beyond the end of the buffer");
            }

            bool hasIndirectionTable = (_current.SliceFlags & EncodingDefinitions.SliceFlags.HasIndirectionTable) != 0;

            // With the 1.1 encoding, SkipSlice for a class skips the indirection table and preserves its position in
            // _current.DeferredIndirectionTableList for later decoding.
            if (_current.InstanceType == InstanceType.Class)
            {
                _current.DeferredIndirectionTableList ??= new List<int>();
                if (hasIndirectionTable)
                {
                    int savedPos = Pos;
                    SkipIndirectionTable();
                    _current.DeferredIndirectionTableList.Add(savedPos); // we want to later read the deepest first
                }
                else
                {
                    _current.DeferredIndirectionTableList.Add(0); // keep a slot for each slice
                }
            }
            else if (hasIndirectionTable)
            {
                Debug.Assert(_current.PosAfterIndirectionTable != null);
                // Move past indirection table
                Pos = _current.PosAfterIndirectionTable.Value;
                _current.PosAfterIndirectionTable = null;
            }

            _current.Slices ??= new List<SliceInfo>();
            var info = new SliceInfo(typeId,
                                     new ReadOnlyMemory<byte>(bytes),
                                     Array.AsReadOnly(_current.IndirectionTable ?? Array.Empty<AnyClass>()),
                                     hasTaggedMembers);
            _current.Slices.Add(info);

            // If we decoded the indirection table previously, we don't need it anymore since we're skipping this slice.
            _current.IndirectionTable = null;

            return (_current.SliceFlags & EncodingDefinitions.SliceFlags.IsLastSlice) != 0;
        }

        private struct InstanceData
        {
            // Instance fields

            internal List<int>? DeferredIndirectionTableList;
            internal InstanceType InstanceType;
            internal List<SliceInfo>? Slices; // Preserved slices.

            // Slice fields

            internal bool FirstSlice; // for now, used only for exceptions
            internal AnyClass[]? IndirectionTable; // Indirection table of the current slice
            internal int? PosAfterIndirectionTable;

            internal EncodingDefinitions.SliceFlags SliceFlags;
            internal int SliceSize;
        }

        private enum InstanceType : byte
        {
            None = 0,
            Class,
            Exception
        }
    }
}
