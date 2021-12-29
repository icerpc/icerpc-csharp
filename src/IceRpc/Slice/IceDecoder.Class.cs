// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice.Internal;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;

using static IceRpc.Slice.Internal.Ice11Definitions;

namespace IceRpc.Slice
{
    // Class-related methods for IceDecoder.
    public ref partial struct IceDecoder
    {
        /// <summary>Decodes a class instance.</summary>
        /// <returns>The decoded class instance.</returns>
        public T DecodeClass<T>() where T : AnyClass =>
            DecodeNullableClass<T>() ??
               throw new InvalidDataException("decoded a null class instance, but expected a non-null instance");

        /// <summary>Decodes a nullable class instance.</summary>
        /// <returns>The class instance, or null.</returns>
        public T? DecodeNullableClass<T>() where T : class
        {
            if (Encoding != IceRpc.Encoding.Ice11)
            {
                throw new InvalidOperationException(
                    $"{nameof(DecodeNullableClass)} is not compatible with encoding {Encoding}");
            }

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
                throw new InvalidDataException(@$"decoded instance of type '{obj.GetType()
                    }' but expected instance of type '{typeof(T)}'");
            }
        }

         /// <summary>Tells the decoder the end of a class or remote exception slice was reached.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void IceEndSlice()
        {
            if (Encoding != IceRpc.Encoding.Ice11)
            {
                throw new InvalidOperationException(
                    $"{nameof(IceEndSlice)} is not compatible with encoding {Encoding}");
            }

            // Note that IceEndSlice is not called when we call SkipSlice.
            Debug.Assert(_classContext.Current.InstanceType != InstanceType.None);

            if ((_classContext.Current.SliceFlags & SliceFlags.HasTaggedMembers) != 0)
            {
                SkipTaggedParams();
            }
            if ((_classContext.Current.SliceFlags & SliceFlags.HasIndirectionTable) != 0)
            {
                Debug.Assert(_classContext.Current.PosAfterIndirectionTable != null &&
                             _classContext.Current.IndirectionTable != null);

                _reader.Advance(_classContext.Current.PosAfterIndirectionTable.Value - _reader.Consumed);
                _classContext.Current.PosAfterIndirectionTable = null;
                _classContext.Current.IndirectionTable = null;
            }
        }

        /// <summary>Marks the start of the decoding of a class or remote exception slice.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public void IceStartSlice()
        {
            if (Encoding != IceRpc.Encoding.Ice11)
            {
                throw new InvalidOperationException(
                    $"{nameof(IceStartSlice)} is not compatible with encoding {Encoding}");
            }

            Debug.Assert(_classContext.Current.InstanceType != InstanceType.None);
            if (_classContext.Current.FirstSlice)
            {
                _classContext.Current.FirstSlice = false;
            }
            else
            {
                _ = DecodeSliceHeaderIntoCurrent();
                DecodeIndirectionTableIntoCurrent();
            }
        }

        /// <summary>Decodes a class instance.</summary>
        /// <returns>The class instance. Can be null.</returns>
        private AnyClass? DecodeAnyClass()
        {
            Debug.Assert(Encoding == IceRpc.Encoding.Ice11);

            int index = DecodeSize();
            if (index < 0)
            {
                throw new InvalidDataException($"invalid index {index} while decoding a class");
            }
            else if (index == 0)
            {
                return null;
            }
            else if (_classContext.Current.InstanceType != InstanceType.None &&
                (_classContext.Current.SliceFlags & SliceFlags.HasIndirectionTable) != 0)
            {
                // When decoding an instance within a slice and there is an indirection table, we have an index within
                // this indirection table.
                // We need to decrement index since position 0 in the indirection table corresponds to index 1.
                index--;
                if (index < _classContext.Current.IndirectionTable?.Length)
                {
                    return _classContext.Current.IndirectionTable[index];
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

        private RemoteException DecodeExceptionClass()
        {
            Debug.Assert(Encoding == IceRpc.Encoding.Ice11);

            // When the response is received over ice1, Ice1ProtocolConnection inserts this reply status. The response
            // can alternatively come straight from an ice2 frame.
            ReplyStatus replyStatus = this.DecodeReplyStatus();

            if (replyStatus == ReplyStatus.OK)
            {
                throw new InvalidDataException("invalid exception with OK ReplyStatus");
            }

            if (replyStatus > ReplyStatus.UserException)
            {
                RemoteException systemException;

                switch (replyStatus)
                {
                    case ReplyStatus.FacetNotExistException:
                    case ReplyStatus.ObjectNotExistException:
                    case ReplyStatus.OperationNotExistException:

                        var requestFailed = new Ice1RequestFailedExceptionData(ref this);
                        requestFailed.Facet.CheckValue();

                        systemException = replyStatus == ReplyStatus.OperationNotExistException ?
                            new OperationNotFoundException() : new ServiceNotFoundException();

                        systemException.Origin = new RemoteExceptionOrigin(
                            requestFailed.Identity.ToPath(),
                            requestFailed.Facet.ToFragment(),
                            requestFailed.Operation);
                        break;

                    default:
                        systemException = new UnhandledException(DecodeString());
                        break;
                }

                systemException.ConvertToUnhandled = true;
                return systemException;
            }
            else
            {
                Debug.Assert(_classContext.Current.InstanceType == InstanceType.None);
                _classContext.Current.InstanceType = InstanceType.Exception;

                RemoteException? remoteEx;

                // We can decode the indirection table (if there is one) immediately after decoding each slice header
                // because the indirection table cannot reference the exception itself.
                // Each slice contains its type ID as a string.

                string? mostDerivedTypeId = null;

                do
                {
                    // The type ID is always decoded for an exception and cannot be null.
                    string? typeId = DecodeSliceHeaderIntoCurrent();
                    Debug.Assert(typeId != null);
                    mostDerivedTypeId ??= typeId;

                    DecodeIndirectionTableIntoCurrent(); // we decode the indirection table immediately.

                    remoteEx = _activator?.CreateInstance(typeId, ref this) as RemoteException;
                    if (remoteEx == null && SkipSlice(typeId)) // Slice off what we don't understand.
                    {
                        break;
                    }
                }
                while (remoteEx == null);

                if (remoteEx != null)
                {
                    _classContext.Current.FirstSlice = true;
                    remoteEx.Decode(ref this);
                }
                else
                {
                    remoteEx = new UnknownSlicedRemoteException(mostDerivedTypeId);
                }

                _classContext.Current = default;
                return remoteEx;
            }
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
            Debug.Assert(_classContext.Current.IndirectionTable == null);
            if ((_classContext.Current.SliceFlags & SliceFlags.HasIndirectionTable) != 0)
            {
                if ((_classContext.Current.SliceFlags & SliceFlags.HasSliceSize) == 0)
                {
                    throw new InvalidDataException("slice has indirection table but does not have a size");
                }

                long savedPos = _reader.Consumed;
                _reader.Advance(_classContext.Current.SliceSize);
                _classContext.Current.IndirectionTable = DecodeIndirectionTable();
                _classContext.Current.PosAfterIndirectionTable = _reader.Consumed;
                _reader.Rewind(_reader.Consumed - savedPos);
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
                if (_classContext.InstanceMap != null && _classContext.InstanceMap.Count > index - 2)
                {
                    return _classContext.InstanceMap[index - 2];
                }
                throw new InvalidDataException($"could not find index {index} in {nameof(_classContext.InstanceMap)}");
            }

            if (++_classContext.ClassGraphDepth > _classContext.ClassGraphMaxDepth)
            {
                throw new InvalidDataException("maximum class graph depth reached");
            }

            // Save current in case we're decoding a nested instance.
            InstanceData previousCurrent = _classContext.Current;
            _classContext.Current = default;
            _classContext.Current.InstanceType = InstanceType.Class;

            AnyClass? instance = null;
            _classContext.InstanceMap ??= new List<AnyClass>();

            bool decodeIndirectionTable = true;
            do
            {
                // Decode the slice header.
                string? typeId = DecodeSliceHeaderIntoCurrent();

                // We cannot decode the indirection table at this point as it may reference the new instance that is
                // not created yet.
                if (typeId != null)
                {
                    instance = _activator?.CreateInstance(typeId, ref this) as AnyClass;
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
            _classContext.InstanceMap!.Add(instance);

            // Decode all the deferred indirection tables now that the instance is inserted in _instanceMap.
            if (_classContext.Current.DeferredIndirectionTableList?.Count > 0)
            {
                long savedPos = _reader.Consumed;

                Debug.Assert(_classContext.Current.Slices?.Count ==
                    _classContext.Current.DeferredIndirectionTableList.Count);
                for (int i = 0; i < _classContext.Current.DeferredIndirectionTableList.Count; ++i)
                {
                    long pos = _classContext.Current.DeferredIndirectionTableList[i];
                    if (pos > 0)
                    {
                        long distance = pos - _reader.Consumed;
                        if (distance > 0)
                        {
                            _reader.Advance(distance);
                        }
                        else
                        {
                            _reader.Rewind(-distance);
                        }
                        _classContext.Current.Slices[i].Instances = Array.AsReadOnly(DecodeIndirectionTable());
                    }
                    // else remains empty
                }

                _reader.Advance(savedPos - _reader.Consumed);
            }

            if (decodeIndirectionTable)
            {
                DecodeIndirectionTableIntoCurrent();
            }

            instance.UnknownSlices = _classContext.Current.Slices?.ToImmutableList() ?? ImmutableList<SliceInfo>.Empty;
            _classContext.Current.FirstSlice = true;
            instance.Decode(ref this);

            _classContext.Current = previousCurrent;
            --_classContext.ClassGraphDepth;
            return instance;
        }

        /// <summary>Decodes the header of the current slice into _current.</summary>
        /// <returns>The type ID or the compact ID of the current slice.</returns>
        private string? DecodeSliceHeaderIntoCurrent()
        {
            _classContext.Current.SliceFlags = (SliceFlags)DecodeByte();

            string? typeId;
            // Decode the type ID. For class slices, the type ID is encoded as a string or as an index or as a compact
            // ID, for exceptions it's always encoded as a string.
            if (_classContext.Current.InstanceType == InstanceType.Class)
            {
                typeId = DecodeTypeId(_classContext.Current.SliceFlags.GetTypeIdKind());

                if (typeId == null)
                {
                    if ((_classContext.Current.SliceFlags & SliceFlags.HasSliceSize) != 0)
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
            if ((_classContext.Current.SliceFlags & SliceFlags.HasSliceSize) != 0)
            {
                _classContext.Current.SliceSize = DecodeSliceSize();
            }
            else
            {
                _classContext.Current.SliceSize = 0;
            }

            // Clear other per-slice fields:
            _classContext.Current.IndirectionTable = null;
            _classContext.Current.PosAfterIndirectionTable = null;

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
        private string? DecodeTypeId(TypeIdKind typeIdKind)
        {
            _classContext.TypeIdMap ??= new List<string>();

            switch (typeIdKind)
            {
                case TypeIdKind.Index:
                    int index = DecodeSize();
                    if (index > 0 && index - 1 < _classContext.TypeIdMap.Count)
                    {
                        // The encoded type-id indexes start at 1, not 0.
                        return _classContext.TypeIdMap[index - 1];
                    }
                    throw new InvalidDataException($"decoded invalid type ID index {index}");

                case TypeIdKind.String:
                    string typeId = DecodeString();

                    // The typeIds of slices in indirection tables can be decoded several times: when we skip the
                    // indirection table and later on when we decode it. We only want to add this typeId to the list and
                    // assign it an index when it's the first time we decode it, so we save the largest position we
                    // decode to figure out when to add to the list.
                    if (_reader.Consumed > _classContext.PosAfterLatestInsertedTypeId)
                    {
                        _classContext.PosAfterLatestInsertedTypeId = _reader.Consumed;
                        _classContext.TypeIdMap.Add(typeId);
                    }
                    return typeId;

                case TypeIdKind.CompactId:
                    return DecodeSize().ToString(CultureInfo.InvariantCulture);

                default:
                    // TypeIdKind has only 4 possible values.
                    Debug.Assert(typeIdKind == TypeIdKind.None);
                    return null;
            }
        }

        /// <summary>Skips the indirection table. The caller must save the current position before calling
        /// SkipIndirectionTable (to decode the indirection table at a later point) except when the caller is
        /// SkipIndirectionTable itself.</summary>
        private void SkipIndirectionTable()
        {
            // We should never skip an exception's indirection table
            Debug.Assert(_classContext.Current.InstanceType == InstanceType.Class);

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
                    if (++_classContext.ClassGraphDepth > _classContext.ClassGraphMaxDepth)
                    {
                        throw new InvalidDataException("maximum class graph depth reached");
                    }

                    // Decode/skip this instance
                    SliceFlags sliceFlags;
                    do
                    {
                        sliceFlags = (SliceFlags)DecodeByte();

                        // Skip type ID - can update _typeIdMap
                        _ = DecodeTypeId(sliceFlags.GetTypeIdKind());

                        // Decode the slice size, then skip the slice
                        if ((sliceFlags & SliceFlags.HasSliceSize) == 0)
                        {
                            throw new InvalidDataException("size of slice missing");
                        }
                        _reader.Advance(DecodeSliceSize());

                        // If this slice has an indirection table, skip it too.
                        if ((sliceFlags & SliceFlags.HasIndirectionTable) != 0)
                        {
                            SkipIndirectionTable();
                        }
                    }
                    while ((sliceFlags & SliceFlags.IsLastSlice) == 0);
                    _classContext.ClassGraphDepth--;
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

            if ((_classContext.Current.SliceFlags & SliceFlags.HasSliceSize) == 0)
            {
                string kind = _classContext.Current.InstanceType.ToString().ToLowerInvariant();
                throw new InvalidDataException(@$"no {kind} found for type ID '{typeId
                        }' and compact format prevents slicing (the sender should use the sliced format instead)");
            }

            bool hasTaggedMembers = (_classContext.Current.SliceFlags & SliceFlags.HasTaggedMembers) != 0;
            byte[] bytes;
            if (hasTaggedMembers)
            {
                // Don't include the tag end marker. It will be re-written by IceEndSlice when the sliced data is
                // re-written.
                bytes = new byte[_classContext.Current.SliceSize - 1];
                CopyTo(bytes.AsSpan());
                Skip(1);
            }
            else
            {
                bytes = new byte[_classContext.Current.SliceSize];
                CopyTo(bytes.AsSpan());
            }

            bool hasIndirectionTable = (_classContext.Current.SliceFlags & SliceFlags.HasIndirectionTable) != 0;

            // With the 1.1 encoding, SkipSlice for a class skips the indirection table and preserves its position in
            // _current.DeferredIndirectionTableList for later decoding.
            if (_classContext.Current.InstanceType == InstanceType.Class)
            {
                _classContext.Current.DeferredIndirectionTableList ??= new List<long>();
                if (hasIndirectionTable)
                {
                    long savedPos = _reader.Consumed;
                    SkipIndirectionTable();

                    // we want to later read the deepest first
                    _classContext.Current.DeferredIndirectionTableList.Add(savedPos);
                }
                else
                {
                    _classContext.Current.DeferredIndirectionTableList.Add(0); // keep a slot for each slice
                }
            }
            else if (hasIndirectionTable)
            {
                Debug.Assert(_classContext.Current.PosAfterIndirectionTable != null);
                // Move past indirection table
                _reader.Advance(_classContext.Current.PosAfterIndirectionTable.Value - _reader.Consumed);
                _classContext.Current.PosAfterIndirectionTable = null;
            }

            _classContext.Current.Slices ??= new List<SliceInfo>();
            var info = new SliceInfo(typeId,
                                     new ReadOnlyMemory<byte>(bytes),
                                     Array.AsReadOnly(_classContext.Current.IndirectionTable ??
                                        Array.Empty<AnyClass>()),
                                     hasTaggedMembers);
            _classContext.Current.Slices.Add(info);

            // If we decoded the indirection table previously, we don't need it anymore since we're skipping this slice.
            _classContext.Current.IndirectionTable = null;

            return (_classContext.Current.SliceFlags & SliceFlags.IsLastSlice) != 0;
        }

        /// <summary>Holds various fields used for class and exception decoding with the Slice 1.1 encoding.</summary>
        private struct ClassContext
        {
            internal readonly int ClassGraphMaxDepth;

            // Data for the class or exception instance that is currently getting decoded.
            internal InstanceData Current;

            // The current depth when decoding nested class instances.
            internal int ClassGraphDepth;

            // Map of class instance ID to class instance.
            // When decoding a buffer:
            //  - Instance ID = 0 means null
            //  - Instance ID = 1 means the instance is encoded inline afterwards
            //  - Instance ID > 1 means a reference to a previously decoded instance, found in this map.
            // Since the map is actually a list, we use instance ID - 2 to lookup an instance.
            internal List<AnyClass>? InstanceMap;

            // See DecodeTypeId.
            internal long PosAfterLatestInsertedTypeId;

            // Map of type ID index to type ID sequence, used only for classes.
            // We assign a type ID index (starting with 1) to each type ID (type ID sequence) we decode, in order.
            // Since this map is a list, we lookup a previously assigned type ID (type ID sequence) with
            // _typeIdMap[index - 1].
            internal List<string>? TypeIdMap;

            internal ClassContext(int classGraphMaxDepth)
                : this()
            {
                if (classGraphMaxDepth < 1 && classGraphMaxDepth != -1)
                {
                    throw new ArgumentException(
                        $"{nameof(classGraphMaxDepth)} must be -1 or greater than 1",
                        nameof(classGraphMaxDepth));
                }

                ClassGraphMaxDepth = classGraphMaxDepth == -1 ? 100 : classGraphMaxDepth;
            }
        }

        private struct InstanceData
        {
            // Instance fields

            internal List<long>? DeferredIndirectionTableList;
            internal InstanceType InstanceType;
            internal List<SliceInfo>? Slices; // Preserved slices.

            // Slice fields

            internal bool FirstSlice;
            internal AnyClass[]? IndirectionTable; // Indirection table of the current slice
            internal long? PosAfterIndirectionTable;

            internal SliceFlags SliceFlags;
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
