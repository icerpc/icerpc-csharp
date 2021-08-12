// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

namespace IceRpc
{
    // This partial class provides the class/exception encoding logic.

    public sealed partial class IceEncoder
    {
        /// <summary>Marks the end of a slice for a class instance or user exception. This is an Ice-internal method
        /// marked public because it's called by the generated code.</summary>
        /// <param name="lastSlice">True when it's the last (least derived) slice of the instance; otherwise, false.
        /// </param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void IceEndSlice(bool lastSlice)
        {
            Debug.Assert(_current.InstanceType != InstanceType.None);

            if (lastSlice)
            {
                _current.SliceFlags |= EncodingDefinitions.SliceFlags.IsLastSlice;
            }

            // Encodes the tagged end marker if some tagged members were encoded. Note that tagged members are encoded
            // before the indirection table and are included in the slice size.
            if ((_current.SliceFlags & EncodingDefinitions.SliceFlags.HasTaggedMembers) != 0)
            {
                EncodeByte(EncodingDefinitions.TaggedEndMarker);
            }

            // Encodes the slice size if necessary.
            if ((_current.SliceFlags & EncodingDefinitions.SliceFlags.HasSliceSize) != 0)
            {
                if (OldEncoding)
                {
                    // Size includes the size length.
                    EncodeFixedLengthSize11(Distance(_current.SliceSizePos), _current.SliceSizePos);
                }
                else
                {
                    // Size does not include the size length.
                    EncodeFixedLengthSize20(Distance(_current.SliceSizePos) - DefaultSizeLength,
                        _current.SliceSizePos);
                }
            }

            if (_current.IndirectionTable?.Count > 0)
            {
                Debug.Assert(_classFormat == FormatType.Sliced);
                _current.SliceFlags |= EncodingDefinitions.SliceFlags.HasIndirectionTable;

                EncodeSize(_current.IndirectionTable.Count);
                foreach (AnyClass v in _current.IndirectionTable)
                {
                    EncodeInstance(v);
                }
                _current.IndirectionTable.Clear();
                _current.IndirectionMap?.Clear(); // IndirectionMap is null when encoding SlicedData.
            }

            // Update SliceFlags in case they were updated.
            RewriteByte((byte)_current.SliceFlags, _current.SliceFlagsPos);
        }

        /// <summary>Starts encoding the first slice of a class or exception instance. This is an Ice-internal method
        /// marked public because it's called by the generated code.</summary>
        /// <param name="allTypeIds">The type IDs of all slices of the instance (excluding sliced-off slices), from
        /// most derived to least derived.</param>
        /// <param name="slicedData">The preserved sliced-off slices, if any.</param>
        /// <param name="errorMessage">The exception error message (provided only by exceptions).</param>
        /// <param name="origin">The exception origin (provided only by exceptions).</param>
        /// <param name="compactTypeId ">The compact ID of this slice, if any. Used by the 1.1 encoding.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void IceStartFirstSlice(
            string[] allTypeIds,
            SlicedData? slicedData = null,
            string? errorMessage = null,
            RemoteExceptionOrigin? origin = null,
            int? compactTypeId = null)
        {
            Debug.Assert(_current.InstanceType != InstanceType.None);

            if (slicedData is SlicedData slicedDataValue)
            {
                bool firstSliceWritten = false;
                try
                {
                    // WriteSlicedData calls IceStartFirstSlice.
                    EncodeSlicedData(slicedDataValue, allTypeIds, errorMessage, origin);
                    firstSliceWritten = true;
                }
                catch (NotSupportedException)
                {
                    // For some reason we could not re-encode the sliced data; firstSliceWritten remains false.
                }
                if (firstSliceWritten)
                {
                    IceStartNextSlice(allTypeIds[0], compactTypeId);
                    return;
                }
                // else keep going, we're still encoding the first slice and we're ignoring slicedData.
            }

            if (OldEncoding && _classFormat == FormatType.Sliced)
            {
                // With the 1.1 encoding in sliced format, all the slice headers are the same.
                IceStartNextSlice(allTypeIds[0], compactTypeId);
            }
            else
            {
                _current.SliceFlags = default;
                _current.SliceFlagsPos = _tail;
                EncodeByte(0); // Placeholder for the slice flags

                if (OldEncoding)
                {
                    EncodeTypeId11(allTypeIds[0], compactTypeId);
                }
                else
                {
                    EncodeTypeId20(allTypeIds, errorMessage, origin);
                    if (_classFormat == FormatType.Sliced)
                    {
                        // Encode the slice size if using the sliced format.
                        _current.SliceFlags |= EncodingDefinitions.SliceFlags.HasSliceSize;
                        _current.SliceSizePos = StartFixedLengthSize();
                    }
                }
            }
        }

        /// <summary>Starts encoding the next (i.e. not first) slice of a class or exception instance. This is an
        /// Ice-internal method marked public because it's called by the generated code.</summary>
        /// <param name="typeId">The type ID of this slice.</param>
        /// <param name="compactId">The compact ID of this slice, if any. Used by the 1.1 encoding.</param>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void IceStartNextSlice(string typeId, int? compactId = null)
        {
            Debug.Assert(_current.InstanceType != InstanceType.None);

            _current.SliceFlags = default;
            _current.SliceFlagsPos = _tail;
            EncodeByte(0); // Placeholder for the slice flags

            if (OldEncoding && _classFormat == FormatType.Sliced)
            {
                EncodeTypeId11(typeId, compactId);
            }

            if (_classFormat == FormatType.Sliced)
            {
                // Encode the slice size if using the sliced format.
                _current.SliceFlags |= EncodingDefinitions.SliceFlags.HasSliceSize;
                _current.SliceSizePos = StartFixedLengthSize();
            }
        }

        /// <summary>Encodes a class instance.</summary>
        /// <param name="v">The class instance to encode.</param>
        public void EncodeClass(AnyClass v)
        {
            if (_current.InstanceType != InstanceType.None && _classFormat == FormatType.Sliced)
            {
                // If encoding an instance within a slice and using the sliced format, encode an index of that slice's
                // indirection table.
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

        /// <summary>Encodes a remote exception.</summary>
        /// <param name="v">The remote exception to encode.</param>
        public void EncodeException(RemoteException v)
        {
            Debug.Assert(_current.InstanceType == InstanceType.None);
            Debug.Assert(_classFormat == FormatType.Sliced);
            _current.InstanceType = InstanceType.Exception;
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
                EncodeClass(v);
            }
        }

        /// <summary>Encodes sliced-off slices.</summary>
        /// <param name="slicedData">The sliced-off slices to encode.</param>
        /// <param name="baseTypeIds">The type IDs of less derived slices.</param>
        /// <param name="errorMessage">For exceptions, the exception's error message.</param>
        /// <param name="origin">For exceptions, the exception's origin.</param>
        internal void EncodeSlicedData(
            SlicedData slicedData,
            string[] baseTypeIds,
            string? errorMessage = null,
            RemoteExceptionOrigin? origin = null)
        {
            Debug.Assert(_current.InstanceType != InstanceType.None);

            // We only re-encode preserved slices if we are using the sliced format. Otherwise, we ignore the preserved
            // slices, which essentially "slices" the instance into the most-derived type known by the sender.
            if (_classFormat != FormatType.Sliced)
            {
                throw new NotSupportedException($"cannot encode sliced data into payload using {_classFormat} format");
            }
            if (Encoding != slicedData.Encoding)
            {
                throw new NotSupportedException(@$"cannot encode sliced data encoded with encoding {slicedData.Encoding
                    } into payload encoded with encoding {Encoding}");
            }

            bool firstSliceWithNewEncoding = !OldEncoding;

            for (int i = 0; i < slicedData.Slices.Count; ++i)
            {
                SliceInfo sliceInfo = slicedData.Slices[i];

                if (firstSliceWithNewEncoding)
                {
                    firstSliceWithNewEncoding = false;

                    string[] allTypeIds = new string[slicedData.Slices.Count + baseTypeIds.Length];
                    for (int j = 0; j < slicedData.Slices.Count; ++j)
                    {
                        allTypeIds[j] = slicedData.Slices[j].TypeId;
                    }
                    if (baseTypeIds.Length > 0)
                    {
                        baseTypeIds.CopyTo(allTypeIds, slicedData.Slices.Count);
                    }

                    IceStartFirstSlice(allTypeIds, errorMessage: errorMessage, origin: origin);
                }
                else
                {
                    // If TypeId is a compact ID, extract it.
                    int? compactId = null;
                    if (!sliceInfo.TypeId.StartsWith("::"))
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

                    // With the 1.1 encoding in sliced format, IceStartNextSlice is the same as IceStartFirstSlice.
                    IceStartNextSlice(sliceInfo.TypeId, compactId);
                }

                // Writes the bytes associated with this slice.
                WriteByteSpan(sliceInfo.Bytes.Span);

                if (sliceInfo.HasTaggedMembers)
                {
                    _current.SliceFlags |= EncodingDefinitions.SliceFlags.HasTaggedMembers;
                }

                // Make sure to also encode the instance indirection table.
                // These instances will be encoded (and assigned instance IDs) in IceEndSlice.
                if (sliceInfo.Instances.Count > 0)
                {
                    _current.IndirectionTable ??= new List<AnyClass>();
                    Debug.Assert(_current.IndirectionTable.Count == 0);
                    _current.IndirectionTable.AddRange(sliceInfo.Instances);
                }
                IceEndSlice(lastSlice: baseTypeIds.Length == 0 && (i == slicedData.Slices.Count - 1));
            }
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

                v.Encode(this);

                // Restore previous _current.
                _current = previousCurrent;
            }
        }

        /// <summary>Encodes the type ID or compact ID immediately after the slice flags byte, and updates the slice
        /// flags byte as needed.</summary>
        /// <param name="typeId">The type ID of the current slice.</param>
        /// <param name="compactId">The compact ID of the current slice.</param>
        private void EncodeTypeId11(string typeId, int? compactId)
        {
            Debug.Assert(_current.InstanceType != InstanceType.None);

            EncodingDefinitions.TypeIdKind typeIdKind = EncodingDefinitions.TypeIdKind.None;

            if (_current.InstanceType == InstanceType.Class)
            {
                if (compactId is int compactIdValue)
                {
                    typeIdKind = EncodingDefinitions.TypeIdKind.CompactId11;
                    EncodeSize(compactIdValue);
                }
                else
                {
                    int index = RegisterTypeId(typeId);
                    if (index < 0)
                    {
                        typeIdKind = EncodingDefinitions.TypeIdKind.String;
                        EncodeString(typeId);
                    }
                    else
                    {
                        typeIdKind = EncodingDefinitions.TypeIdKind.Index;
                        EncodeSize(index);
                    }
                }
            }
            else
            {
                Debug.Assert(compactId == null);
                // With the 1.1 encoding, we always encode a string and don't set a type ID kind in SliceFlags.
                EncodeString(typeId);
            }

            _current.SliceFlags |= (EncodingDefinitions.SliceFlags)typeIdKind;
        }

        /// <summary>Encodes the type ID or type ID sequence immediately after the slice flags byte of the first slice,
        /// and updates the slice flags byte as needed. Applies formal type optimization (class only), if possible.
        /// </summary>
        /// <param name="allTypeIds">The type IDs of all slices of this class or exception instance.</param>
        /// <param name="errorMessage">The exception's error message. Provided only for exceptions.</param>
        /// <param name="origin">The exception's origin. Provided only for exceptions.</param>
        private void EncodeTypeId20(string[] allTypeIds, string? errorMessage, RemoteExceptionOrigin? origin)
        {
            Debug.Assert(_current.InstanceType != InstanceType.None);

            EncodingDefinitions.TypeIdKind typeIdKind = EncodingDefinitions.TypeIdKind.None;

            if (_current.InstanceType == InstanceType.Class)
            {
                string typeId = allTypeIds[0];
                int index = RegisterTypeId(typeId);
                if (index < 0)
                {
                    if (_classFormat == FormatType.Sliced)
                    {
                        typeIdKind = EncodingDefinitions.TypeIdKind.Sequence20;
                        EncodeSequence(allTypeIds, (encoder, value) => encoder.EncodeString(value));
                    }
                    else
                    {
                        typeIdKind = EncodingDefinitions.TypeIdKind.String;
                        EncodeString(typeId);
                    }
                }
                else
                {
                    typeIdKind = EncodingDefinitions.TypeIdKind.Index;
                    EncodeSize(index);
                }
            }
            else
            {
                typeIdKind = EncodingDefinitions.TypeIdKind.Sequence20;
                EncodeSequence(allTypeIds, (encoder, value) => encoder.EncodeString(value));

                Debug.Assert(errorMessage != null);
                EncodeString(errorMessage);
                Debug.Assert(origin != null);
                origin.Value.Encode(this);
            }

            _current.SliceFlags |= (EncodingDefinitions.SliceFlags)typeIdKind;
        }

        private struct InstanceData
        {
            // The following fields are used and reused for all the slices of a class or exception instance.

            internal InstanceType InstanceType;

            // The following fields are used for the current slice:

            // The indirection map and indirection table are only used for the sliced format.
            internal Dictionary<AnyClass, int>? IndirectionMap;
            internal List<AnyClass>? IndirectionTable;

            internal EncodingDefinitions.SliceFlags SliceFlags;

            // Position of the slice flags.
            internal Position SliceFlagsPos;

            // Position of the slice size. Used only for the sliced format.
            internal Position SliceSizePos;
        }

        private enum InstanceType : byte
        {
            None = 0,
            Class,
            Exception
        }
    }
}
