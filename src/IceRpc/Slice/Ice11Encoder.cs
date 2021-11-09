// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using IceRpc.Transports.Internal;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

using static IceRpc.Slice.Internal.Ice11Definitions;

namespace IceRpc.Slice
{
    /// <summary>Encoder for the Ice 1.1 encoding.</summary>
    public sealed class Ice11Encoder : IceEncoder
    {
        // The current class/exception format, can be either Compact or Sliced.
        private FormatType _classFormat;

        // Data for the class or exception instance that is currently getting encoded.
        private InstanceData _current;

        // Map of class instance to instance ID, where the instance IDs start at 2.
        //  - Instance ID = 0 means null.
        //  - Instance ID = 1 means the instance is encoded inline afterwards.
        //  - Instance ID > 1 means a reference to a previously encoded instance, found in this map.
        private Dictionary<AnyClass, int>? _instanceMap;

        // Map of type ID string to type ID index.
        // We assign a type ID index (starting with 1) to each type ID we write, in order.
        private Dictionary<string, int>? _typeIdMap;

        /// <summary>Encodes a class instance.</summary>
        /// <param name="v">The class instance to encode.</param>
        public void EncodeClass(AnyClass v) => EncodeNullableClass(v);

        /// <inheritdoc/>
        public override void EncodeException(RemoteException v)
        {
            Debug.Assert(_current.InstanceType == InstanceType.None);

            _classFormat = FormatType.Sliced; // always encode exceptions in sliced format
            _current.InstanceType = InstanceType.Exception;
            _current.FirstSlice = true;
            v.Encode(this);
            _current = default;
        }

        /// <summary>Encodes a class instance, or null.</summary>
        /// <param name="v">The class instance to encode, or null.</param>
        public void EncodeNullableClass(AnyClass? v)
        {
            if (v == null)
            {
                EncodeSize(0);
            }
            else
            {
                if (_current.InstanceType != InstanceType.None && _classFormat == FormatType.Sliced)
                {
                    // If encoding an instance within a slice and using the sliced format, encode an index of that
                    // slice's indirection table.
                    if (_current.IndirectionMap != null && _current.IndirectionMap.TryGetValue(v, out int index))
                    {
                        // Found, index is position in indirection table + 1
                        Debug.Assert(index > 0);
                    }
                    else
                    {
                        _current.IndirectionTable ??= new List<AnyClass>();
                        _current.IndirectionMap ??= new Dictionary<AnyClass, int>();
                        _current.IndirectionTable.Add(v);
                        index = _current.IndirectionTable.Count; // Position + 1 (0 is reserved for null)
                        _current.IndirectionMap.Add(v, index);
                    }
                    EncodeSize(index);
                }
                else
                {
                    EncodeInstance(v); // Encodes the instance or a reference if already encoded.
                }
            }
        }

        /// <inheritdoc/>
        public override void EncodeNullableProxy(Proxy? proxy)
        {
            if (proxy == null)
            {
                Identity.Empty.Encode(this);
            }
            else
            {
                if (proxy.Connection?.IsServer ?? false)
                {
                    throw new InvalidOperationException("cannot encode a proxy bound to a server connection");
                }

                IdentityAndFacet identityAndFacet;

                try
                {
                    identityAndFacet = IdentityAndFacet.FromPath(proxy.Path);
                }
                catch (FormatException ex)
                {
                    throw new InvalidOperationException(
                        $"cannot encode proxy with path '{proxy.Path}' using encoding 1.1",
                        ex);
                }

                if (identityAndFacet.Identity.Name.Length == 0)
                {
                    throw new InvalidOperationException(
                        $"cannot encode proxy with path '{proxy.Path}' using encoding 1.1");
                }

                identityAndFacet.Identity.Encode(this);

                (byte encodingMajor, byte encodingMinor) = proxy.Encoding.ToMajorMinor();

                var proxyData = new ProxyData11(
                    identityAndFacet.OptionalFacet,
                    proxy.Protocol == Protocol.Ice1 && (proxy.Endpoint?.Transport == TransportNames.Udp) ?
                        InvocationMode.Datagram : InvocationMode.Twoway,
                    secure: false,
                    protocolMajor: (byte)proxy.Protocol.Code,
                    protocolMinor: 0,
                    encodingMajor,
                    encodingMinor);
                proxyData.Encode(this);

                if (proxy.Endpoint == null)
                {
                    EncodeSize(0); // 0 endpoints
                    EncodeString(""); // empty adapter ID
                }
                else if (proxy.Protocol == Protocol.Ice1 && proxy.Endpoint.Transport == TransportNames.Loc)
                {
                    EncodeSize(0); // 0 endpoints
                    EncodeString(proxy.Endpoint.Host); // adapter ID unless well-known
                }
                else
                {
                    IEnumerable<Endpoint> endpoints = proxy.Endpoint.Transport == TransportNames.Coloc ?
                        proxy.AltEndpoints : Enumerable.Empty<Endpoint>().Append(proxy.Endpoint).Concat(
                            proxy.AltEndpoints);

                    if (endpoints.Any())
                    {
                        this.EncodeSequence(endpoints, (encoder, endpoint) => encoder.EncodeEndpoint(endpoint));
                    }
                    else // encoded as an endpointless proxy
                    {
                        EncodeSize(0); // 0 endpoints
                        EncodeString(""); // empty adapter ID
                    }
                }
            }
        }

        /// <inheritdoc/>
        public override void EncodeSize(int v)
        {
            if (v < 255)
            {
                EncodeByte((byte)v);
            }
            else
            {
                EncodeByte(255);
                EncodeInt(v);
            }
        }

        /// <inheritdoc/>
        public override void EncodeTagged<T>(
            int tag,
            TagFormat tagFormat,
            T v,
            EncodeAction<IceEncoder, T> encodeAction)
        {
            if (tagFormat == TagFormat.FSize)
            {
                EncodeTaggedParamHeader(tag, tagFormat);
                BufferWriter.Position pos = StartFixedLengthSize();
                encodeAction(this, v);
                EndFixedLengthSize(pos);
            }
            else
            {
                // A VSize where the size is optimized out. Used here for strings (and only strings) because we cannot
                // easily compute the number of UTF-8 bytes in a C# string before encoding it.
                Debug.Assert(tagFormat == TagFormat.OVSize);

                EncodeTaggedParamHeader(tag, TagFormat.VSize);
                encodeAction(this, v);
            }
        }

        /// <inheritdoc/>
        public override void EncodeTagged<T>(
            int tag,
            TagFormat tagFormat,
            int size,
            T v,
            EncodeAction<IceEncoder, T> encodeAction)
        {
            Debug.Assert(tagFormat != TagFormat.FSize);
            Debug.Assert(size > 0);

            bool encodeSize = tagFormat == TagFormat.VSize;

            tagFormat = tagFormat switch
            {
                TagFormat.VInt => size switch
                {
                    1 => TagFormat.F1,
                    2 => TagFormat.F2,
                    4 => TagFormat.F4,
                    8 => TagFormat.F8,
                    _ => throw new ArgumentException($"invalid value for size: {size}", nameof(size))
                },

                TagFormat.OVSize => TagFormat.VSize, // size encoding is optimized out

                _ => tagFormat
            };

            EncodeTaggedParamHeader(tag, tagFormat);

            if (encodeSize)
            {
                EncodeSize(size);
            }

            BufferWriter.Position startPos = BufferWriter.Tail;
            encodeAction(this, v);
            int actualSize = BufferWriter.Distance(startPos);
            if (actualSize != size)
            {
                throw new ArgumentException($"value of size ({size}) does not match encoded size ({actualSize})",
                                            nameof(size));
            }
        }

        /// <inheritdoc/>
        public override int GetSizeLength(int size) => size < 255 ? 1 : 5;

        /// <summary>Marks the end of the encoding of a class or exception slice.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void IceEndSlice(bool lastSlice)
        {
            Debug.Assert(_current.InstanceType != InstanceType.None);

            if (lastSlice)
            {
                _current.SliceFlags |= SliceFlags.IsLastSlice;
            }

            // Encodes the tagged end marker if some tagged members were encoded. Note that tagged members are encoded
            // before the indirection table and are included in the slice size.
            if ((_current.SliceFlags & SliceFlags.HasTaggedMembers) != 0)
            {
                EncodeByte(TagEndMarker);
            }

            // Encodes the slice size if necessary.
            if ((_current.SliceFlags & SliceFlags.HasSliceSize) != 0)
            {
                // Size includes the size length.
                EncodeFixedLengthSize(BufferWriter.Distance(_current.SliceSizePos), _current.SliceSizePos);
            }

            if (_current.IndirectionTable?.Count > 0)
            {
                Debug.Assert(_classFormat == FormatType.Sliced);
                _current.SliceFlags |= SliceFlags.HasIndirectionTable;

                EncodeSize(_current.IndirectionTable.Count);
                foreach (AnyClass v in _current.IndirectionTable)
                {
                    EncodeInstance(v);
                }
                _current.IndirectionTable.Clear();
                _current.IndirectionMap?.Clear(); // IndirectionMap is null when encoding unknown slices.
            }

            // Update SliceFlags in case they were updated.
            BufferWriter.RewriteByte((byte)_current.SliceFlags, _current.SliceFlagsPos);
        }

        /// <summary>Marks the start of the encoding of a class or remote exception slice.</summary>
        /// <param name="typeId">The type ID of this slice.</param>
        /// <param name="compactId ">The compact ID of this slice, if any.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void IceStartSlice(string typeId, int? compactId = null)
        {
            Debug.Assert(_current.InstanceType != InstanceType.None);

            _current.SliceFlags = default;
            _current.SliceFlagsPos = BufferWriter.Tail;
            EncodeByte(0); // Placeholder for the slice flags

            if (_classFormat == FormatType.Sliced)
            {
                EncodeTypeId(typeId, compactId);
                // Encode the slice size if using the sliced format.
                _current.SliceFlags |= SliceFlags.HasSliceSize;
                _current.SliceSizePos = StartFixedLengthSize();
            }
            else if (_current.FirstSlice)
            {
                EncodeTypeId(typeId, compactId);
            }

            if (_current.FirstSlice)
            {
                _current.FirstSlice = false;
            }
        }

        /// <summary>Constructs an encoder for the Ice 1.1 encoding.</summary>
        internal Ice11Encoder(BufferWriter bufferWriter, FormatType classFormat = default)
            : base(bufferWriter) =>
            _classFormat = classFormat;

        /// <summary>Encodes sliced-off slices.</summary>
        /// <param name="unknownSlices">The sliced-off slices to encode.</param>
        /// <param name="fullySliced">When true, slicedData holds all the data of this instance.</param>
        internal void EncodeUnknownSlices(ImmutableList<SliceInfo> unknownSlices, bool fullySliced)
        {
            Debug.Assert(_current.InstanceType != InstanceType.None);

            // We only re-encode preserved slices if we are using the sliced format. Otherwise, we ignore the preserved
            // slices, which essentially "slices" the instance into the most-derived type known by the sender.
            if (_classFormat != FormatType.Sliced)
            {
                throw new NotSupportedException($"cannot encode sliced data into payload using {_classFormat} format");
            }

            for (int i = 0; i < unknownSlices.Count; ++i)
            {
                SliceInfo sliceInfo = unknownSlices[i];

                // If TypeId is a compact ID, extract it.
                int? compactId = null;
                if (!sliceInfo.TypeId.StartsWith("::", StringComparison.Ordinal))
                {
                    try
                    {
                        compactId = int.Parse(sliceInfo.TypeId, CultureInfo.InvariantCulture);
                    }
                    catch (FormatException ex)
                    {
                        throw new InvalidDataException($"received invalid type ID {sliceInfo.TypeId}", ex);
                    }
                }

                IceStartSlice(sliceInfo.TypeId, compactId);

                // Writes the bytes associated with this slice.
                BufferWriter.WriteByteSpan(sliceInfo.Bytes.Span);

                if (sliceInfo.HasTaggedMembers)
                {
                    _current.SliceFlags |= SliceFlags.HasTaggedMembers;
                }

                // Make sure to also encode the instance indirection table.
                // These instances will be encoded (and assigned instance IDs) in IceEndSlice.
                if (sliceInfo.Instances.Count > 0)
                {
                    _current.IndirectionTable ??= new List<AnyClass>();
                    Debug.Assert(_current.IndirectionTable.Count == 0);
                    _current.IndirectionTable.AddRange(sliceInfo.Instances);
                }
                IceEndSlice(lastSlice: fullySliced && (i == unknownSlices.Count - 1));
            }
        }

        private protected override void EncodeFixedLengthSize(int size, Span<byte> into)
        {
            if (into.Length != 4)
            {
                throw new ArgumentException($"into has {into.Length} bytes");
            }
            IceEncoder.EncodeInt(size, into);
        }

        /// <summary>Encodes an endpoint in a nested encapsulation.</summary>
        /// <param name="endpoint">The endpoint to encode.</param>
        private void EncodeEndpoint(Endpoint endpoint)
        {
            // The Ice 1.1 encoding of ice1 endpoints is transport-specific, and hard-coded here and in the
            // Ice11Decoder. The preferred and fallback encoding for new transports is TransportCode.Any, which uses an
            // EndpointData like Ice 2.0.

            if (endpoint.Protocol == Protocol.Ice1 && endpoint.Transport == TransportNames.Opaque)
            {
                // Opaque endpoint encoding

                (TransportCode transportCode, Encoding encoding, ReadOnlyMemory<byte> bytes) =
                    endpoint.ParseOpaqueParams();

                this.EncodeTransportCode(transportCode);
                EncodeInt(4 + 2 + bytes.Length); // encapsulation size includes size-length and 2 bytes for encoding
                EncodeByte(1); // encoding version major
                if (encoding == Encoding.Ice11)
                {
                    EncodeByte(1); // encoding version minor
                }
                else
                {
                    Debug.Assert(encoding == Encoding.Ice10);
                    EncodeByte(0); // encoding version minor
                }
                BufferWriter.WriteByteSpan(bytes.Span);
            }
            else
            {
                TransportCode transportCode = endpoint.Protocol == Protocol.Ice1 ?
                    endpoint.Transport switch
                    {
                        TransportNames.Tcp => TransportCode.TCP,
                        TransportNames.Ssl => TransportCode.SSL,
                        TransportNames.Udp => TransportCode.UDP,
                        _ => TransportCode.Any
                    } : TransportCode.Any;

                this.EncodeTransportCode(transportCode);

                BufferWriter.Position startPos = BufferWriter.Tail;
                EncodeInt(0); // placeholder for encapsulation size written by EncodeFixedLengthSize below
                EncodeByte(1); // encoding version major
                EncodeByte(1); // encoding version minor

                switch (transportCode)
                {
                    case TransportCode.TCP:
                    case TransportCode.SSL:
                    {
                        (bool compress, int timeout, bool? _) = endpoint.ParseTcpParams();
                        EncodeString(endpoint.Host);
                        EncodeInt(endpoint.Port);
                        EncodeInt(timeout);
                        EncodeBool(compress);
                        break;
                    }

                    case TransportCode.UDP:
                    {
                        bool compress = endpoint.ParseUdpParams().Compress;
                        EncodeString(endpoint.Host);
                        EncodeInt(endpoint.Port);
                        EncodeBool(compress);
                        break;
                    }

                    default:
                        Debug.Assert(transportCode == TransportCode.Any);
                        endpoint.ToEndpointData().Encode(this);
                        break;
                }

                EncodeFixedLengthSize(BufferWriter.Distance(startPos), startPos);
            }
        }

        /// <summary>Encodes this class instance inline if not previously encoded, otherwise just encode its instance
        /// ID.</summary>
        /// <param name="v">The class instance.</param>
        private void EncodeInstance(AnyClass v)
        {
            // If the instance was already encoded, just encode its instance ID.
            if (_instanceMap != null && _instanceMap.TryGetValue(v, out int instanceId))
            {
                EncodeSize(instanceId);
            }
            else
            {
                _instanceMap ??= new Dictionary<AnyClass, int>();

                // We haven't seen this instance previously, so we create a new instance ID and insert the instance
                // and its ID in the encoded map, before encoding the instance inline.
                // The instance IDs start at 2 (0 means null and 1 means the instance is encoded immediately after).
                instanceId = _instanceMap.Count + 2;
                _instanceMap.Add(v, instanceId);

                EncodeSize(1); // Class instance marker.

                // Save _current in case we're encoding a nested instance.
                InstanceData previousCurrent = _current;
                _current = default;
                _current.InstanceType = InstanceType.Class;
                _current.FirstSlice = true;

                if (v.UnknownSlices.Count > 0 && _classFormat == FormatType.Sliced)
                {
                    EncodeUnknownSlices(v.UnknownSlices, fullySliced: false);
                    _current.FirstSlice = false;
                }
                v.Encode(this);

                // Restore previous _current.
                _current = previousCurrent;
            }
        }

        /// <summary>Encodes the header for a tagged parameter or data member.</summary>
        /// <param name="tag">The numeric tag associated with the parameter or data member.</param>
        /// <param name="format">The tag format.</param>
        private void EncodeTaggedParamHeader(int tag, TagFormat format)
        {
            Debug.Assert(format != TagFormat.VInt && format != TagFormat.OVSize); // VInt/OVSize cannot be encoded

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

            if (_current.InstanceType != InstanceType.None)
            {
                _current.SliceFlags |= SliceFlags.HasTaggedMembers;
            }
        }

        /// <summary>Encodes the type ID or compact ID immediately after the slice flags byte, and updates the slice
        /// flags byte as needed.</summary>
        /// <param name="typeId">The type ID of the current slice.</param>
        /// <param name="compactId">The compact ID of the current slice.</param>
        private void EncodeTypeId(string typeId, int? compactId)
        {
            Debug.Assert(_current.InstanceType != InstanceType.None);

            TypeIdKind typeIdKind = TypeIdKind.None;

            if (_current.InstanceType == InstanceType.Class)
            {
                if (compactId is int compactIdValue)
                {
                    typeIdKind = TypeIdKind.CompactId;
                    EncodeSize(compactIdValue);
                }
                else
                {
                    int index = RegisterTypeId(typeId);
                    if (index < 0)
                    {
                        typeIdKind = TypeIdKind.String;
                        EncodeString(typeId);
                    }
                    else
                    {
                        typeIdKind = TypeIdKind.Index;
                        EncodeSize(index);
                    }
                }
            }
            else
            {
                Debug.Assert(compactId == null);
                // We always encode a string and don't set a type ID kind in SliceFlags.
                EncodeString(typeId);
            }

            _current.SliceFlags |= (SliceFlags)typeIdKind;
        }

        /// <summary>Registers or looks up a type ID in the the _typeIdMap.</summary>
        /// <param name="typeId">The type ID to register or lookup.</param>
        /// <returns>The index in _typeIdMap if this type ID was previously registered; otherwise, -1.</returns>
        private int RegisterTypeId(string typeId)
        {
            _typeIdMap ??= new Dictionary<string, int>();

            if (_typeIdMap.TryGetValue(typeId, out int index))
            {
                return index;
            }
            else
            {
                index = _typeIdMap.Count + 1;
                _typeIdMap.Add(typeId, index);
                return -1;
            }
        }

        private struct InstanceData
        {
            // The following fields are used and reused for all the slices of a class or exception instance.

            internal InstanceType InstanceType;

            // The following fields are used for the current slice:

            internal bool FirstSlice;

            // The indirection map and indirection table are only used for the sliced format.
            internal Dictionary<AnyClass, int>? IndirectionMap;
            internal List<AnyClass>? IndirectionTable;

            internal SliceFlags SliceFlags;

            // Position of the slice flags.
            internal BufferWriter.Position SliceFlagsPos;

            // Position of the slice size. Used only for the sliced format.
            internal BufferWriter.Position SliceSizePos;
        }

        private enum InstanceType : byte
        {
            None = 0,
            Class,
            Exception
        }
    }
}
