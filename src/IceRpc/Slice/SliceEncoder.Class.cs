// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

using static IceRpc.Slice.Internal.Slice11Definitions;

namespace IceRpc.Slice
{
    public ref partial struct SliceEncoder
    {
        /// <summary>Encodes a class instance.</summary>
        /// <param name="v">The class instance to encode.</param>
        public void EncodeClass(AnyClass v) => EncodeNullableClass(v);

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
                if (_classContext.Current.InstanceType != InstanceType.None && _classContext.ClassFormat == FormatType.Sliced)
                {
                    // If encoding an instance within a slice and using the sliced format, encode an index of that
                    // slice's indirection table.
                    if (_classContext.Current.IndirectionMap != null && _classContext.Current.IndirectionMap.TryGetValue(v, out int index))
                    {
                        // Found, index is position in indirection table + 1
                        Debug.Assert(index > 0);
                    }
                    else
                    {
                        _classContext.Current.IndirectionTable ??= new List<AnyClass>();
                        _classContext.Current.IndirectionMap ??= new Dictionary<AnyClass, int>();
                        _classContext.Current.IndirectionTable.Add(v);
                        index = _classContext.Current.IndirectionTable.Count; // Position + 1 (0 is reserved for null)
                        _classContext.Current.IndirectionMap.Add(v, index);
                    }
                    EncodeSize(index);
                }
                else
                {
                    EncodeInstance(v); // Encodes the instance or a reference if already encoded.
                }
            }
        }

        /// <summary>Encodes a dispatch exception as an ice "system exception".</summary>
        /// <param name="v">The dispatch exception to encode.</param>
        public void EncodeDispatchExceptionAsSystemException(DispatchException v)
        {
            Debug.Assert(Encoding == IceRpc.Encoding.Slice11);

            DispatchErrorCode errorCode = v.ErrorCode;

            switch (errorCode)
            {
                case DispatchErrorCode.ServiceNotFound:
                case DispatchErrorCode.OperationNotFound:
                    this.EncodeReplyStatus(errorCode == DispatchErrorCode.ServiceNotFound ?
                        ReplyStatus.ObjectNotExistException : ReplyStatus.OperationNotExistException);

                    // TODO: pass context to dispatch exception Encode
                    var requestFailed = new RequestFailedExceptionData(path: "/", "", "");
                    requestFailed.Encode(ref this);
                    break;

                default:
                    this.EncodeReplyStatus(ReplyStatus.UnknownException);
                    // We encode the error code in the message.
                    EncodeString($"[{((byte)errorCode).ToString(CultureInfo.InvariantCulture)}] {v.Message}");
                    break;
            }
        }

        /// <summary>Marks the end of the encoding of a class or exception slice.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void EndSlice(bool lastSlice)
        {
            Debug.Assert(_classContext.Current.InstanceType != InstanceType.None);

            if (lastSlice)
            {
                _classContext.Current.SliceFlags |= SliceFlags.IsLastSlice;
            }

            // Encodes the tagged end marker if some tagged members were encoded. Note that tagged members are encoded
            // before the indirection table and are included in the slice size.
            if ((_classContext.Current.SliceFlags & SliceFlags.HasTaggedMembers) != 0)
            {
                EncodeByte(Slice11Definitions.TagEndMarker);
            }

            // Encodes the slice size if necessary.
            if ((_classContext.Current.SliceFlags & SliceFlags.HasSliceSize) != 0)
            {
                // Size includes the size length.
                EncodeInt(EncodedByteCount - _classContext.Current.SliceSizeStartPos, _classContext.Current.SliceSizePlaceholder.Span);
            }

            if (_classContext.Current.IndirectionTable?.Count > 0)
            {
                Debug.Assert(_classContext.ClassFormat == FormatType.Sliced);
                _classContext.Current.SliceFlags |= SliceFlags.HasIndirectionTable;

                EncodeSize(_classContext.Current.IndirectionTable.Count);
                foreach (AnyClass v in _classContext.Current.IndirectionTable)
                {
                    EncodeInstance(v);
                }
                _classContext.Current.IndirectionTable.Clear();
                _classContext.Current.IndirectionMap?.Clear(); // IndirectionMap is null when encoding unknown slices.
            }

            // Update SliceFlags in case they were updated.
            _classContext.Current.SliceFlagsPlaceholder.Span[0] = (byte)_classContext.Current.SliceFlags;

            // If this is the last slice in an exception, reset the current context.
            if (lastSlice && _classContext.Current.InstanceType == InstanceType.Exception)
            {
                _classContext.Current = default;
            }
        }

        /// <summary>Marks the start of the encoding of a class or remote exception slice.</summary>
        /// <param name="typeId">The type ID of this slice.</param>
        /// <param name="compactId ">The compact ID of this slice, if any.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void StartSlice(string typeId, int? compactId = null)
        {
            // This will only be called with an InstanceType of 'None' when we're starting to encode the first slice
            // of a remote exception.
            if (_classContext.Current.InstanceType == InstanceType.None)
            {
                _classContext.ClassFormat = FormatType.Sliced; // always encode exceptions in sliced format
                _classContext.Current.InstanceType = InstanceType.Exception;
                _classContext.Current.FirstSlice = true;
            }

            _classContext.Current.SliceFlags = default;
            _classContext.Current.SliceFlagsPlaceholder = GetPlaceholderMemory(1);

            if (_classContext.ClassFormat == FormatType.Sliced)
            {
                EncodeTypeId(typeId, compactId);
                // Encode the slice size if using the sliced format.
                _classContext.Current.SliceFlags |= SliceFlags.HasSliceSize;
                _classContext.Current.SliceSizeStartPos = EncodedByteCount; // size includes size-length
                _classContext.Current.SliceSizePlaceholder = GetPlaceholderMemory(4);
            }
            else if (_classContext.Current.FirstSlice)
            {
                EncodeTypeId(typeId, compactId);
            }

            if (_classContext.Current.FirstSlice)
            {
                _classContext.Current.FirstSlice = false;
            }
        }

        /// <summary>Encodes sliced-off slices.</summary>
        /// <param name="unknownSlices">The sliced-off slices to encode.</param>
        /// <param name="fullySliced">When true, slicedData holds all the data of this instance.</param>
        internal void EncodeUnknownSlices(ImmutableList<SliceInfo> unknownSlices, bool fullySliced)
        {
            Debug.Assert(_classContext.Current.InstanceType != InstanceType.None);

            // We only re-encode preserved slices if we are using the sliced format. Otherwise, we ignore the preserved
            // slices, which essentially "slices" the instance into the most-derived type known by the sender.
            if (_classContext.ClassFormat != FormatType.Sliced)
            {
                throw new NotSupportedException($"cannot encode sliced data into payload using {_classContext.ClassFormat} format");
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

                StartSlice(sliceInfo.TypeId, compactId);

                // Writes the bytes associated with this slice.
                WriteByteSpan(sliceInfo.Bytes.Span);

                if (sliceInfo.HasTaggedMembers)
                {
                    _classContext.Current.SliceFlags |= SliceFlags.HasTaggedMembers;
                }

                // Make sure to also encode the instance indirection table.
                // These instances will be encoded (and assigned instance IDs) in EndSlice.
                if (sliceInfo.Instances.Count > 0)
                {
                    _classContext.Current.IndirectionTable ??= new List<AnyClass>();
                    Debug.Assert(_classContext.Current.IndirectionTable.Count == 0);
                    _classContext.Current.IndirectionTable.AddRange(sliceInfo.Instances);
                }
                EndSlice(lastSlice: fullySliced && (i == unknownSlices.Count - 1));
            }
        }

        /// <summary>Encodes this class instance inline if not previously encoded, otherwise just encode its instance
        /// ID.</summary>
        /// <param name="v">The class instance.</param>
        private void EncodeInstance(AnyClass v)
        {
            // If the instance was already encoded, just encode its instance ID.
            if (_classContext.InstanceMap != null && _classContext.InstanceMap.TryGetValue(v, out int instanceId))
            {
                EncodeSize(instanceId);
            }
            else
            {
                _classContext.InstanceMap ??= new Dictionary<AnyClass, int>();

                // We haven't seen this instance previously, so we create a new instance ID and insert the instance
                // and its ID in the encoded map, before encoding the instance inline.
                // The instance IDs start at 2 (0 means null and 1 means the instance is encoded immediately after).
                instanceId = _classContext.InstanceMap.Count + 2;
                _classContext.InstanceMap.Add(v, instanceId);

                EncodeSize(1); // Class instance marker.

                // Save _current in case we're encoding a nested instance.
                InstanceData previousCurrent = _classContext.Current;
                _classContext.Current = default;
                _classContext.Current.InstanceType = InstanceType.Class;
                _classContext.Current.FirstSlice = true;

                if (v.UnknownSlices.Count > 0 && _classContext.ClassFormat == FormatType.Sliced)
                {
                    EncodeUnknownSlices(v.UnknownSlices, fullySliced: false);
                    _classContext.Current.FirstSlice = false;
                }
                v.Encode(ref this);

                // Restore previous _current.
                _classContext.Current = previousCurrent;
            }
        }

        /// <summary>Encodes the type ID or compact ID immediately after the slice flags byte, and updates the slice
        /// flags byte as needed.</summary>
        /// <param name="typeId">The type ID of the current slice.</param>
        /// <param name="compactId">The compact ID of the current slice.</param>
        private void EncodeTypeId(string typeId, int? compactId)
        {
            Debug.Assert(_classContext.Current.InstanceType != InstanceType.None);

            TypeIdKind typeIdKind = TypeIdKind.None;

            if (_classContext.Current.InstanceType == InstanceType.Class)
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

            _classContext.Current.SliceFlags |= (SliceFlags)typeIdKind;
        }

        /// <summary>Registers or looks up a type ID in the the _typeIdMap.</summary>
        /// <param name="typeId">The type ID to register or lookup.</param>
        /// <returns>The index in _typeIdMap if this type ID was previously registered; otherwise, -1.</returns>
        private int RegisterTypeId(string typeId)
        {
            _classContext.TypeIdMap ??= new Dictionary<string, int>();

            if (_classContext.TypeIdMap.TryGetValue(typeId, out int index))
            {
                return index;
            }
            else
            {
                index = _classContext.TypeIdMap.Count + 1;
                _classContext.TypeIdMap.Add(typeId, index);
                return -1;
            }
        }

        private struct ClassContext
        {
            // The current class/exception format, can be either Compact or Sliced.
            internal FormatType ClassFormat;

            // Data for the class or exception instance that is currently getting encoded.
            internal InstanceData Current;

            // Map of class instance to instance ID, where the instance IDs start at 2.
            //  - Instance ID = 0 means null.
            //  - Instance ID = 1 means the instance is encoded inline afterwards.
            //  - Instance ID > 1 means a reference to a previously encoded instance, found in this map.
            internal Dictionary<AnyClass, int>? InstanceMap;

            // Map of type ID string to type ID index.
            // We assign a type ID index (starting with 1) to each type ID we write, in order.
            internal Dictionary<string, int>? TypeIdMap;

            internal ClassContext(FormatType classFormat)
                : this() => ClassFormat = classFormat;
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

            // The slice flags byte.
            internal Memory<byte> SliceFlagsPlaceholder;

            // The place holder for the Slice size. Used only for the sliced format.
            internal Memory<byte> SliceSizePlaceholder;

            // The starting position for computing the size of the slice. It's just before the SliceSizePlaceholder as
            // the size includes the size length.
            internal int SliceSizeStartPos;
        }

        private enum InstanceType : byte
        {
            None = 0,
            Class,
            Exception
        }
    }
}
