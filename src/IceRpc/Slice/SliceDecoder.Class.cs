// Copyright (c) ZeroC, Inc.

using IceRpc.Slice.Internal;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;

using static IceRpc.Slice.Internal.Slice1Definitions;

namespace IceRpc.Slice;

/// <summary>Provides methods to decode data encoded with Slice1 or Slice2.</summary>
public ref partial struct SliceDecoder
{
    /// <summary>Decodes a class instance.</summary>
    /// <typeparam name="T">The class type.</typeparam>
    /// <returns>The decoded class instance.</returns>
    public T DecodeClass<T>() where T : SliceClass =>
        DecodeNullableClass<T>() ??
           throw new InvalidDataException("Decoded a null class instance, but expected a non-null instance.");

    /// <summary>Decodes a nullable class instance.</summary>
    /// <typeparam name="T">The class type.</typeparam>
    /// <returns>The class instance, or <see langword="null" />.</returns>
    public T? DecodeNullableClass<T>() where T : class
    {
        if (Encoding != SliceEncoding.Slice1)
        {
            throw new InvalidOperationException($"{nameof(DecodeNullableClass)} is not compatible with {Encoding}.");
        }

        SliceClass? obj = DecodeClass();

        if (obj is T result)
        {
            return result;
        }
        else if (obj is null)
        {
            return null;
        }
        throw new InvalidDataException(
            $"Decoded instance of type '{obj.GetType()}' but expected instance of type '{typeof(T)}'.");
    }

    /// <summary>Decodes a Slice1 user exception.</summary>
    /// <param name="message">The error message.</param>
    /// <returns>The decoded Slice exception or a <see cref="DispatchException" /> with status code
    /// <see cref="StatusCode.ApplicationError" /> if the decoder's activator cannot find an exception class for the
    /// type ID encoded in the underlying buffer.</returns>
    public DispatchException DecodeUserException(string? message = null)
    {
        if (Encoding != SliceEncoding.Slice1)
        {
            throw new InvalidOperationException($"{nameof(DecodeUserException)} is not compatible with {Encoding}.");
        }

        Debug.Assert(_classContext.Current.InstanceType == InstanceType.None);
        _classContext.Current.InstanceType = InstanceType.Exception;

        SliceException? sliceException;

        // We can decode the indirection table (if there is one) immediately after decoding each slice header
        // because the indirection table cannot reference the exception itself.
        // Each slice contains its type ID as a string.

        string? mostDerivedTypeId = null;
        IActivator activator = _activator ?? _defaultActivator;

        do
        {
            // The type ID is always decoded for an exception and cannot be null.
            string? typeId = DecodeSliceHeaderIntoCurrent();
            Debug.Assert(typeId is not null);
            mostDerivedTypeId ??= typeId;

            DecodeIndirectionTableIntoCurrent(); // we decode the indirection table immediately.

            sliceException = activator.CreateExceptionInstance(typeId, ref this, message) as SliceException;
            if (sliceException is null && SkipSlice(typeId))
            {
                // Cannot decode this exception.
                break;
            }
        }
        while (sliceException is null);

        if (sliceException is null)
        {
            return new DispatchException(
                StatusCode.ApplicationError,
                message ??
                    $"The dispatch returned a Slice exception with type ID '{mostDerivedTypeId}' and the local runtime cannot decode this exception.")
            {
                ConvertToUnhandled = true
            };
        }
        else
        {
            _classContext.Current.FirstSlice = true;
            sliceException.Decode(ref this);
            _classContext.Current = default;
            return sliceException;
        }
    }

    /// <summary>Tells the decoder the end of a class or exception slice was reached.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void EndSlice()
    {
        if (Encoding != SliceEncoding.Slice1)
        {
            throw new InvalidOperationException($"{nameof(EndSlice)} is not compatible with encoding {Encoding}.");
        }

        // Note that EndSlice is not called when we call SkipSlice.
        Debug.Assert(_classContext.Current.InstanceType != InstanceType.None);

        if ((_classContext.Current.SliceFlags & SliceFlags.HasTaggedFields) != 0)
        {
            SkipTagged(useTagEndMarker: true);
        }
        if ((_classContext.Current.SliceFlags & SliceFlags.HasIndirectionTable) != 0)
        {
            Debug.Assert(_classContext.Current.PosAfterIndirectionTable is not null &&
                         _classContext.Current.IndirectionTable is not null);

            _reader.Advance(_classContext.Current.PosAfterIndirectionTable.Value - _reader.Consumed);
            _classContext.Current.PosAfterIndirectionTable = null;
            _classContext.Current.IndirectionTable = null;
        }
    }

    /// <summary>Marks the start of the decoding of a class or remote exception slice.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public void StartSlice()
    {
        if (Encoding != SliceEncoding.Slice1)
        {
            throw new InvalidOperationException($"{nameof(StartSlice)} is not compatible with encoding {Encoding}.");
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
    /// <returns>The class instance. Can be <see langword="null" />.</returns>
    private SliceClass? DecodeClass()
    {
        Debug.Assert(Encoding == SliceEncoding.Slice1);

        int index = DecodeSize();
        if (index < 0)
        {
            throw new InvalidDataException($"Found invalid index {index} while decoding a class.");
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
                throw new InvalidDataException("The index is too big for the indirection table.");
            }
        }
        else
        {
            return DecodeInstance(index);
        }
    }

    /// <summary>Decodes an indirection table without updating _current.</summary>
    /// <returns>The indirection table.</returns>
    private SliceClass[] DecodeIndirectionTable()
    {
        int size = DecodeSize();
        if (size == 0)
        {
            throw new InvalidDataException("Invalid empty indirection table.");
        }
        IncreaseCollectionAllocation(size * Unsafe.SizeOf<SliceClass>());
        var indirectionTable = new SliceClass[size];
        for (int i = 0; i < indirectionTable.Length; ++i)
        {
            int index = DecodeSize();
            if (index < 1)
            {
                throw new InvalidDataException($"Found invalid index {index} decoding the indirection table.");
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
        Debug.Assert(_classContext.Current.IndirectionTable is null);
        if ((_classContext.Current.SliceFlags & SliceFlags.HasIndirectionTable) != 0)
        {
            if ((_classContext.Current.SliceFlags & SliceFlags.HasSliceSize) == 0)
            {
                throw new InvalidDataException("The Slice has indirection table flag but has not size flag.");
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
    /// seen class; if 1, the class instance's bytes are next. Cannot be 0 or less.</param>
    private SliceClass DecodeInstance(int index)
    {
        Debug.Assert(index > 0);

        if (index > 1)
        {
            if (_classContext.InstanceMap is not null && _classContext.InstanceMap.Count > index - 2)
            {
                return _classContext.InstanceMap[index - 2];
            }
            throw new InvalidDataException($"Cannot find instance index {index} in the instance map.");
        }

        if (++_currentDepth > _maxDepth)
        {
            throw new InvalidDataException("The maximum decoder depth was reached while decoding a class.");
        }

        // Save current in case we're decoding a nested instance.
        InstanceData previousCurrent = _classContext.Current;
        _classContext.Current = default;
        _classContext.Current.InstanceType = InstanceType.Class;

        SliceClass? instance = null;
        _classContext.InstanceMap ??= new List<SliceClass>();

        bool decodeIndirectionTable = true;
        IActivator activator = _activator ?? _defaultActivator;
        do
        {
            // Decode the slice header.
            string? typeId = DecodeSliceHeaderIntoCurrent();

            // We cannot decode the indirection table at this point as it may reference the new instance that is
            // not created yet.
            if (typeId is not null)
            {
                instance = activator.CreateClassInstance(typeId, ref this) as SliceClass;
            }

            if (instance is null && SkipSlice(typeId))
            {
                // Slice off what we don't understand.
                instance = new UnknownSlicedClass();
                // Don't decode the indirection table as it's the last entry in DeferredIndirectionTableList.
                decodeIndirectionTable = false;
            }
        }
        while (instance is null);

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
                    _classContext.Current.Slices[i].Instances = DecodeIndirectionTable();
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
        --_currentDepth;
        return instance;
    }

    /// <summary>Decodes the header of the current slice into _current.</summary>
    /// <returns>The type ID or the compact ID of the current slice.</returns>
    private string? DecodeSliceHeaderIntoCurrent()
    {
        _classContext.Current.SliceFlags = (SliceFlags)DecodeUInt8();

        string? typeId;
        // Decode the type ID. For class slices, the type ID is encoded as a string or as an index or as a compact
        // ID, for exceptions it's always encoded as a string.
        if (_classContext.Current.InstanceType == InstanceType.Class)
        {
            typeId = DecodeTypeId(_classContext.Current.SliceFlags.GetTypeIdKind());

            if (typeId is null)
            {
                if ((_classContext.Current.SliceFlags & SliceFlags.HasSliceSize) != 0)
                {
                    throw new InvalidDataException(
                        "Invalid Slice flags; a Slice in compact format cannot carry a size.");
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
        int size = DecodeInt32();
        if (size < 4)
        {
            throw new InvalidDataException($"Invalid Slice size: {size}.");
        }
        // With Slice1, the encoded size includes the size length.
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
                throw new InvalidDataException($"Decoded invalid type ID index {index}.");

            case TypeIdKind.String:
                string typeId = DecodeString();

                // The typeIds of slices in indirection tables can be decoded several times: when we skip the
                // indirection table and later on when we decode it. We only want to add this type ID to the list and
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

        int tableSize = DecodeSize();
        for (int i = 0; i < tableSize; ++i)
        {
            int index = DecodeSize();
            if (index <= 0)
            {
                throw new InvalidDataException($"Decoded invalid index {index} in indirection table.");
            }
            if (index == 1)
            {
                if (++_currentDepth > _maxDepth)
                {
                    throw new InvalidDataException("Maximum decoder depth reached while decoding a class.");
                }

                // Decode/skip this instance
                SliceFlags sliceFlags;
                do
                {
                    sliceFlags = (SliceFlags)DecodeUInt8();

                    // Skip type ID - can update _typeIdMap
                    _ = DecodeTypeId(sliceFlags.GetTypeIdKind());

                    // Decode the slice size, then skip the slice
                    if ((sliceFlags & SliceFlags.HasSliceSize) == 0)
                    {
                        throw new InvalidDataException("The Slice size flag is missing.");
                    }
                    _reader.Advance(DecodeSliceSize());

                    // If this slice has an indirection table, skip it too.
                    if ((sliceFlags & SliceFlags.HasIndirectionTable) != 0)
                    {
                        SkipIndirectionTable();
                    }
                }
                while ((sliceFlags & SliceFlags.IsLastSlice) == 0);
                _currentDepth--;
            }
        }
    }

    /// <summary>Skips and saves the body of the current slice (save only for classes); also skips and save the
    /// indirection table (if any).</summary>
    /// <param name="typeId">The type ID or compact ID of the current slice.</param>
    /// <returns><see langword="true" /> when the current slice is the last slice; otherwise, <see langword="false" />.
    /// </returns>
    private bool SkipSlice(string? typeId)
    {
        if (typeId is null)
        {
            throw new InvalidDataException("Cannot skip a class slice with no type ID.");
        }

        if ((_classContext.Current.SliceFlags & SliceFlags.HasSliceSize) == 0)
        {
            // If it's an exception in compact format, we just return true, and the caller (DecodeUserException) will
            // return a DispatchException.
            return _classContext.Current.InstanceType == InstanceType.Exception ? true :
                throw new InvalidDataException(
                    $"No class found for type ID '{typeId}' and compact format prevents slicing (the sender should use the sliced format instead).");
        }

        bool hasTaggedFields = (_classContext.Current.SliceFlags & SliceFlags.HasTaggedFields) != 0;
        byte[] bytes;
        if (hasTaggedFields)
        {
            // Don't include the tag end marker. It will be re-written by SliceEncoder.EndSlice when the sliced data
            // is re-written.
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

        // With Slice1, SkipSlice for a class skips the indirection table and preserves its position in
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

            var info = new SliceInfo(
                typeId,
                new ReadOnlyMemory<byte>(bytes),
                _classContext.Current.IndirectionTable ?? Array.Empty<SliceClass>(),
                hasTaggedFields);

            _classContext.Current.Slices ??= new List<SliceInfo>();
            _classContext.Current.Slices.Add(info);
        }
        else if (hasIndirectionTable)
        {
            Debug.Assert(_classContext.Current.PosAfterIndirectionTable is not null);

            // Move past indirection table
            _reader.Advance(_classContext.Current.PosAfterIndirectionTable.Value - _reader.Consumed);
            _classContext.Current.PosAfterIndirectionTable = null;
        }

        // If we decoded the indirection table previously, we don't need it anymore since we're skipping this slice.
        _classContext.Current.IndirectionTable = null;

        return (_classContext.Current.SliceFlags & SliceFlags.IsLastSlice) != 0;
    }

    /// <summary>Holds various fields used for class and exception decoding with Slice1.</summary>
    private struct ClassContext
    {
        // Data for the class or exception instance that is currently getting decoded.
        internal InstanceData Current;

        // Map of class instance ID to class instance.
        // When decoding a buffer:
        //  - Instance ID = 0 means null
        //  - Instance ID = 1 means the instance is encoded inline afterwards
        //  - Instance ID > 1 means a reference to a previously decoded instance, found in this map.
        // Since the map is actually a list, we use instance ID - 2 to lookup an instance.
        internal List<SliceClass>? InstanceMap;

        // See DecodeTypeId.
        internal long PosAfterLatestInsertedTypeId;

        // Map of type ID index to type ID sequence, used only for classes.
        // We assign a type ID index (starting with 1) to each type ID (type ID sequence) we decode, in order.
        // Since this map is a list, we lookup a previously assigned type ID (type ID sequence) with
        // _typeIdMap[index - 1].
        internal List<string>? TypeIdMap;
    }

    private struct InstanceData
    {
        // Instance fields

        internal List<long>? DeferredIndirectionTableList;
        internal InstanceType InstanceType;
        internal List<SliceInfo>? Slices; // Preserved slices.

        // Slice fields

        internal bool FirstSlice;
        internal SliceClass[]? IndirectionTable; // Indirection table of the current slice
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
