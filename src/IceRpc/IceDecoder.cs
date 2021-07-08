// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Transports;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc
{
    /// <summary>Decodes a byte buffer encoded using the Ice encoding.</summary>
    public sealed partial class IceDecoder
    {
        /// <summary>The Ice encoding used by this decoder when reading its byte buffer.</summary>
        /// <value>The encoding.</value>
        public Encoding Encoding { get; }

        /// <summary>Connection used when unmarshaling proxies.</summary>
        internal Connection? Connection { get; }

        /// <summary>Invoker used when unmarshaling proxies.</summary>
        internal IInvoker? Invoker { get; }

        /// <summary>The 0-based position (index) in the underlying buffer.</summary>
        internal int Pos { get; private set; }

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
                    return new SlicedData(Encoding, _current.Slices);
                }
            }
        }

        private bool OldEncoding => Encoding == Encoding.V11;

        // The byte buffer we are reading.
        private readonly ReadOnlyMemory<byte> _buffer;

        private readonly int _classGraphMaxDepth;

        private IReadOnlyDictionary<int, Lazy<ClassFactory>>? _compactTypeIdClassFactories;

        // Data for the class or exception instance that is currently getting unmarshaled.
        private InstanceData _current;

        // The current depth when reading nested class instances.
        private int _classGraphDepth;

        // Map of class instance ID to class instance.
        // When reading a buffer:
        //  - Instance ID = 0 means null
        //  - Instance ID = 1 means the instance is encoded inline afterwards
        //  - Instance ID > 1 means a reference to a previously read instance, found in this map.
        // Since the map is actually a list, we use instance ID - 2 to lookup an instance.
        private List<AnyClass>? _instanceMap;

        // The sum of all the minimum sizes (in bytes) of the sequences read in this buffer. Must not exceed the buffer
        // size.
        private int _minTotalSeqSize;

        // See DecodeTypeId11.
        private int _posAfterLatestInsertedTypeId11;

        IReadOnlyDictionary<string, Lazy<ClassFactory>>? _typeIdClassFactories;
        IReadOnlyDictionary<string, Lazy<RemoteExceptionFactory>>? _typeIdRemoteExceptionFactories;

        // Map of type ID index to type ID sequence, used only for classes.
        // We assign a type ID index (starting with 1) to each type ID (type ID sequence) we read, in order.
        // Since this map is a list, we lookup a previously assigned type ID (type ID sequence) with
        // _typeIdMap[index - 1]. With the 2.0 encoding, each entry has at least 1 element.
        private List<string>? _typeIdMap11;
        private List<string[]>? _typeIdMap20;

        // Decode methods for basic types

        /// <summary>Decodes a bool from the buffer.</summary>
        /// <returns>The bool read from the buffer.</returns>
        public bool DecodeBool() => _buffer.Span[Pos++] == 1;

        /// <summary>Decodes a byte from the buffer.</summary>
        /// <returns>The byte read from the buffer.</returns>
        public byte DecodeByte() => _buffer.Span[Pos++];

        /// <summary>Decodes a double from the buffer.</summary>
        /// <returns>The double read from the buffer.</returns>
        public double DecodeDouble()
        {
            double value = BitConverter.ToDouble(_buffer.Span.Slice(Pos, sizeof(double)));
            Pos += sizeof(double);
            return value;
        }

        /// <summary>Decodes a float from the buffer.</summary>
        /// <returns>The float read from the buffer.</returns>
        public float DecodeFloat()
        {
            float value = BitConverter.ToSingle(_buffer.Span.Slice(Pos, sizeof(float)));
            Pos += sizeof(float);
            return value;
        }

        /// <summary>Decodes an int from the buffer.</summary>
        /// <returns>The int read from the buffer.</returns>
        public int DecodeInt()
        {
            int value = BitConverter.ToInt32(_buffer.Span.Slice(Pos, sizeof(int)));
            Pos += sizeof(int);
            return value;
        }

        /// <summary>Decodes a long from the buffer.</summary>
        /// <returns>The long read from the buffer.</returns>
        public long DecodeLong()
        {
            long value = BitConverter.ToInt64(_buffer.Span.Slice(Pos, sizeof(long)));
            Pos += sizeof(long);
            return value;
        }

        /// <summary>Decodes a short from the buffer.</summary>
        /// <returns>The short read from the buffer.</returns>
        public short DecodeShort()
        {
            short value = BitConverter.ToInt16(_buffer.Span.Slice(Pos, sizeof(short)));
            Pos += sizeof(short);
            return value;
        }

        /// <summary>Decodes a size from the buffer. This size's encoding is variable-length.</summary>
        /// <returns>The size read from the buffer.</returns>
        public int DecodeSize() => OldEncoding ? DecodeSize11() : DecodeSize20();

        /// <summary>Decodes a string from the buffer.</summary>
        /// <returns>The string read from the buffer.</returns>
        public string DecodeString()
        {
            int size = DecodeSize();
            if (size == 0)
            {
                return "";
            }
            else
            {
                string value = _buffer.Slice(Pos, size).Span.DecodeString();
                Pos += size;
                return value;
            }
        }

        /// <summary>Decodes a uint from the buffer.</summary>
        /// <returns>The uint read from the buffer.</returns>
        public uint DecodeUInt()
        {
            uint value = BitConverter.ToUInt32(_buffer.Span.Slice(Pos, sizeof(uint)));
            Pos += sizeof(uint);
            return value;
        }

        /// <summary>Decodes a ulong from the buffer.</summary>
        /// <returns>The ulong read from the buffer.</returns>
        public ulong DecodeULong()
        {
            ulong value = BitConverter.ToUInt64(_buffer.Span.Slice(Pos, sizeof(ulong)));
            Pos += sizeof(ulong);
            return value;
        }

        /// <summary>Decodes a ushort from the buffer.</summary>
        /// <returns>The ushort read from the buffer.</returns>
        public ushort DecodeUShort()
        {
            ushort value = BitConverter.ToUInt16(_buffer.Span.Slice(Pos, sizeof(ushort)));
            Pos += sizeof(ushort);
            return value;
        }

        /// <summary>Decodes an int from the buffer. This int is encoded using Ice's variable-size integer encoding.
        /// </summary>
        /// <returns>The int read from the buffer.</returns>
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

        /// <summary>Decodes a long from the buffer. This long is encoded using Ice's variable-size integer encoding.
        /// </summary>
        /// <returns>The long read from the buffer.</returns>
        public long DecodeVarLong() =>
            (_buffer.Span[Pos] & 0x03) switch
            {
                0 => (sbyte)DecodeByte() >> 2,
                1 => DecodeShort() >> 2,
                2 => DecodeInt() >> 2,
                _ => DecodeLong() >> 2
            };

        /// <summary>Decodes a uint from the buffer. This uint is encoded using Ice's variable-size integer encoding.
        /// </summary>
        /// <returns>The uint read from the buffer.</returns>
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

        /// <summary>Decodes a ulong from the buffer. This ulong is encoded using Ice's variable-size integer encoding.
        /// </summary>
        /// <returns>The ulong read from the buffer.</returns>
        public ulong DecodeVarULong() =>
            (_buffer.Span[Pos] & 0x03) switch
            {
                0 => (uint)DecodeByte() >> 2,   // cast to uint to use operator >> for uint instead of int, which is
                1 => (uint)DecodeUShort() >> 2, // later implicitly converted to ulong
                2 => DecodeUInt() >> 2,
                _ => DecodeULong() >> 2
            };

        // Decode methods for constructed types except class and exception

        /// <summary>Decodes a sequence of fixed-size numeric values from the buffer and returns an array.</summary>
        /// <returns>The sequence read from the buffer, as an array.</returns>
        public T[] DecodeArray<T>() where T : struct
        {
            int elementSize = Unsafe.SizeOf<T>();
            var value = new T[DecodeAndCheckSeqSize(elementSize)];
            int byteCount = elementSize * value.Length;
            _buffer.Span.Slice(Pos, byteCount).CopyTo(MemoryMarshal.Cast<T, byte>(value));
            Pos += byteCount;
            return value;
        }

        /// <summary>Decodes a sequence of fixed-size numeric values from the buffer and returns an array.</summary>
        /// <param name="checkElement">A delegate use to checks each element of the array.</param>
        /// <returns>The sequence read from the buffer, as an array.</returns>
        public T[] DecodeArray<T>(Action<T> checkElement) where T : struct
        {
            T[] value = DecodeArray<T>();
            foreach (T e in value)
            {
                checkElement(e);
            }
            return value;
        }

        /// <summary>Decodes a sequence from the buffer and returns an array.</summary>
        /// <param name="minElementSize">The minimum size of each element of the sequence, in bytes.</param>
        /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
        /// <returns>The sequence read from the buffer, as an array.</returns>
        public T[] DecodeArray<T>(int minElementSize, IceDecodeFunc<T> decodeFunc) =>
            DecodeSequence(minElementSize, decodeFunc).ToArray();

        /// <summary>Decodes a sequence of nullable elements from the buffer and returns an array.</summary>
        /// <param name="withBitSequence">True when null elements are encoded using a bit sequence; otherwise, false.
        /// </param>
        /// <param name="decodeFunc">The decode function for each non-null element of the sequence.</param>
        /// <returns>The sequence read from the buffer, as an array.</returns>
        public T?[] DecodeArray<T>(bool withBitSequence, IceDecodeFunc<T> decodeFunc) where T : class =>
            DecodeSequence(withBitSequence, decodeFunc).ToArray();

        /// <summary>Decodes a sequence of nullable values from the buffer and returns an array.</summary>
        /// <param name="decodeFunc">The decode function for each non-null element of the sequence.</param>
        /// <returns>The sequence read from the buffer, as an array.</returns>
        public T?[] DecodeArray<T>(IceDecodeFunc<T> decodeFunc) where T : struct => DecodeSequence(decodeFunc).ToArray();

        /// <summary>Decodes a dictionary from the buffer.</summary>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="minValueSize">The minimum size of each value of the dictionary, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
        /// <returns>The dictionary read from the buffer.</returns>
        public Dictionary<TKey, TValue> DecodeDictionary<TKey, TValue>(
            int minKeySize,
            int minValueSize,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
        {
            int sz = DecodeAndCheckSeqSize(minKeySize + minValueSize);
            var dict = new Dictionary<TKey, TValue>(sz);
            for (int i = 0; i < sz; ++i)
            {
                TKey key = keyDecodeFunc(this);
                TValue value = valueDecodeFunc(this);
                dict.Add(key, value);
            }
            return dict;
        }

        /// <summary>Decodes a dictionary from the buffer.</summary>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="withBitSequence">When true, null dictionary values are encoded using a bit sequence.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary read from the buffer.</returns>
        public Dictionary<TKey, TValue?> DecodeDictionary<TKey, TValue>(
            int minKeySize,
            bool withBitSequence,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : class
        {
            int sz = DecodeAndCheckSeqSize(minKeySize);
            return DecodeDictionary(new Dictionary<TKey, TValue?>(sz), sz, withBitSequence, keyDecodeFunc, valueDecodeFunc);
        }

        /// <summary>Decodes a dictionary from the buffer.</summary>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary read from the buffer.</returns>
        public Dictionary<TKey, TValue?> DecodeDictionary<TKey, TValue>(
            int minKeySize,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : struct
        {
            int sz = DecodeAndCheckSeqSize(minKeySize);
            return DecodeDictionary(new Dictionary<TKey, TValue?>(sz), sz, keyDecodeFunc, valueDecodeFunc);
        }

        /// <summary>Decodes a sequence from the buffer.</summary>
        /// <param name="minElementSize">The minimum size of each element of the sequence, in bytes.</param>
        /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
        /// <returns>A collection that provides the size of the sequence and allows you read the sequence from the
        /// the buffer. The return value does not fully implement ICollection{T}, in particular you can only call
        /// GetEnumerator() once on this collection. You would typically use this collection to construct a List{T} or
        /// some other generic collection that can be constructed from an IEnumerable{T}.</returns>
        public ICollection<T> DecodeSequence<T>(int minElementSize, IceDecodeFunc<T> decodeFunc) =>
            new Collection<T>(this, minElementSize, decodeFunc);

        /// <summary>Decodes a sequence of nullable elements from the buffer. The element type is a reference type.
        /// </summary>
        /// <param name="withBitSequence">True when null elements are encoded using a bit sequence; otherwise, false.
        /// </param>
        /// <param name="decodeFunc">The decode function for each non-null element of the sequence.</param>
        /// <returns>A collection that provides the size of the sequence and allows you read the sequence from the
        /// the buffer. The returned collection does not fully implement ICollection{T?}, in particular you can only
        /// call GetEnumerator() once on this collection. You would typically use this collection to construct a
        /// List{T?} or some other generic collection that can be constructed from an IEnumerable{T?}.</returns>
        public ICollection<T?> DecodeSequence<T>(bool withBitSequence, IceDecodeFunc<T> decodeFunc) where T : class =>
            withBitSequence ? new NullableCollection<T>(this, decodeFunc) : (ICollection<T?>)DecodeSequence(1, decodeFunc);

        /// <summary>Decodes a sequence of nullable values from the buffer.</summary>
        /// <param name="decodeFunc">The decode function for each non-null element (value) of the sequence.
        /// </param>
        /// <returns>A collection that provides the size of the sequence and allows you read the sequence from the
        /// the buffer. The returned collection does not fully implement ICollection{T?}, in particular you can only
        /// call GetEnumerator() once on this collection. You would typically use this collection to construct a
        /// List{T?} or some other generic collection that can be constructed from an IEnumerable{T?}.</returns>
        public ICollection<T?> DecodeSequence<T>(IceDecodeFunc<T> decodeFunc) where T : struct =>
            new NullableValueCollection<T>(this, decodeFunc);

        /// <summary>Decodes a sorted dictionary from the buffer.</summary>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="minValueSize">The minimum size of each value of the dictionary, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
        /// <returns>The sorted dictionary read from the buffer.</returns>
        public SortedDictionary<TKey, TValue> DecodeSortedDictionary<TKey, TValue>(
            int minKeySize,
            int minValueSize,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
        {
            int sz = DecodeAndCheckSeqSize(minKeySize + minValueSize);
            var dict = new SortedDictionary<TKey, TValue>();
            for (int i = 0; i < sz; ++i)
            {
                TKey key = keyDecodeFunc(this);
                TValue value = valueDecodeFunc(this);
                dict.Add(key, value);
            }
            return dict;
        }

        /// <summary>Decodes a sorted dictionary from the buffer.</summary>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="withBitSequence">When true, null dictionary values are encoded using a bit sequence.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.
        /// </param>
        /// <returns>The sorted dictionary read from the buffer.</returns>
        public SortedDictionary<TKey, TValue?> DecodeSortedDictionary<TKey, TValue>(
            int minKeySize,
            bool withBitSequence,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : class =>
            DecodeDictionary(
                new SortedDictionary<TKey, TValue?>(),
                DecodeAndCheckSeqSize(minKeySize),
                withBitSequence,
                keyDecodeFunc,
                valueDecodeFunc);

        /// <summary>Decodes a sorted dictionary from the buffer. The dictionary's value type is a nullable value type.
        /// </summary>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The sorted dictionary read from the buffer.</returns>
        public SortedDictionary<TKey, TValue?> DecodeSortedDictionary<TKey, TValue>(
            int minKeySize,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : struct =>
            DecodeDictionary(
                new SortedDictionary<TKey, TValue?>(),
                DecodeAndCheckSeqSize(minKeySize),
                keyDecodeFunc,
                valueDecodeFunc);

        // Decode methods for tagged basic types

        /// <summary>Decodes a tagged bool from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The bool read from the buffer, or null.</returns>
        public bool? DecodeTaggedBool(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F1) ? DecodeBool() : (bool?)null;

        /// <summary>Decodes a tagged byte from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The byte read from the buffer, or null.</returns>
        public byte? DecodeTaggedByte(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F1) ? DecodeByte() : (byte?)null;

        /// <summary>Decodes a tagged double from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The double read from the buffer, or null.</returns>
        public double? DecodeTaggedDouble(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F8) ? DecodeDouble() : (double?)null;

        /// <summary>Decodes a tagged float from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The float read from the buffer, or null.</returns>
        public float? DecodeTaggedFloat(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F4) ? DecodeFloat() : (float?)null;

        /// <summary>Decodes a tagged int from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The int read from the buffer, or null.</returns>
        public int? DecodeTaggedInt(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F4) ? DecodeInt() : (int?)null;

        /// <summary>Decodes a tagged long from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The long read from the buffer, or null.</returns>
        public long? DecodeTaggedLong(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F8) ? DecodeLong() : (long?)null;

        /// <summary>Decodes a tagged short from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The short read from the buffer, or null.</returns>
        public short? DecodeTaggedShort(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F2) ? DecodeShort() : (short?)null;

        /// <summary>Decodes a tagged size from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The size read from the buffer, or null.</returns>
        public int? DecodeTaggedSize(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.Size) ? DecodeSize() : (int?)null;

        /// <summary>Decodes a tagged string from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The string read from the buffer, or null.</returns>
        public string? DecodeTaggedString(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize) ? DecodeString() : null;

        /// <summary>Decodes a tagged uint from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The uint read from the buffer, or null.</returns>
        public uint? DecodeTaggedUInt(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F4) ? DecodeUInt() : (uint?)null;

        /// <summary>Decodes a tagged ulong from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The ulong read from the buffer, or null.</returns>
        public ulong? DecodeTaggedULong(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F8) ? DecodeULong() : (ulong?)null;

        /// <summary>Decodes a tagged ushort from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The ushort read from the buffer, or null.</returns>
        public ushort? DecodeTaggedUShort(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F2) ? DecodeUShort() : (ushort?)null;

        /// <summary>Decodes a tagged varint from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The int read from the buffer, or null.</returns>
        public int? DecodeTaggedVarInt(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VInt) ? DecodeVarInt() : (int?)null;

        /// <summary>Decodes a tagged varlong from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The long read from the buffer, or null.</returns>
        public long? DecodeTaggedVarLong(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VInt) ? DecodeVarLong() : (long?)null;

        /// <summary>Decodes a tagged varuint from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The uint read from the buffer, or null.</returns>
        public uint? DecodeTaggedVarUInt(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VInt) ? DecodeVarUInt() : (uint?)null;

        /// <summary>Decodes a tagged varulong from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The ulong read from the buffer, or null.</returns>
        public ulong? DecodeTaggedVarULong(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VInt) ? DecodeVarULong() : (ulong?)null;

        // Decode methods for tagged constructed types except class

        /// <summary>Decodes a tagged array of a fixed-size numeric type from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The sequence read from the buffer as an array, or null.</returns>
        public T[]? DecodeTaggedArray<T>(int tag) where T : struct
        {
            int elementSize = Unsafe.SizeOf<T>();
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize))
            {
                if (elementSize > 1)
                {
                    // For elements with size > 1, the encoding includes a size (number of bytes in the tagged
                    // parameter) that we skip.
                    SkipSize();
                }
                return DecodeArray<T>();
            }
            else
            {
                return null;
            }
        }

        /// <summary>Decodes a tagged array of a fixed-size numeric type from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="checkElement">A delegate use to checks each element of the array.</param>
        /// <returns>The sequence read from the buffer as an array, or null.</returns>
        public T[]? DecodeTaggedArray<T>(int tag, Action<T> checkElement) where T : struct
        {
            int elementSize = Unsafe.SizeOf<T>();
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize))
            {
                if (elementSize > 1)
                {
                    // For elements with size > 1, the encoding includes a size (number of bytes in the tagged
                    // parameter) that we skip.
                    SkipSize();
                }
                return DecodeArray(checkElement);
            }
            else
            {
                return null;
            }
        }

        /// <summary>Decodes a tagged array from the buffer. The element type can be nullable only if it corresponds to
        /// a proxy class or mapped Slice class.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minElementSize">The minimum size of each element, in bytes.</param>
        /// <param name="fixedSize">True when the element size is fixed; otherwise, false.</param>
        /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
        /// <returns>The sequence read from the buffer as an array, or null.</returns>
        public T[]? DecodeTaggedArray<T>(int tag, int minElementSize, bool fixedSize, IceDecodeFunc<T> decodeFunc) =>
            DecodeTaggedSequence(tag, minElementSize, fixedSize, decodeFunc)?.ToArray();

        /// <summary>Decodes a tagged array of nullable elements from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="withBitSequence">True when null elements are encoded using a bit sequence; otherwise, false.
        /// </param>
        /// <param name="decodeFunc">The decode function for each non-null element of the array.</param>
        /// <returns>The array read from the buffer, or null.</returns>
        public T?[]? DecodeTaggedArray<T>(int tag, bool withBitSequence, IceDecodeFunc<T> decodeFunc) where T : class =>
            DecodeTaggedSequence(tag, withBitSequence, decodeFunc)?.ToArray();

        /// <summary>Decodes a tagged array of nullable values from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="decodeFunc">The decode function for each non-null value of the array.</param>
        /// <returns>The array read from the buffer, or null.</returns>
        public T?[]? DecodeTaggedArray<T>(int tag, IceDecodeFunc<T> decodeFunc) where T : struct =>
            DecodeTaggedSequence(tag, decodeFunc)?.ToArray();

        /// <summary>Decodes a tagged dictionary from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minKeySize">The minimum size of each key, in bytes.</param>
        /// <param name="minValueSize">The minimum size of each value, in bytes.</param>
        /// <param name="fixedSize">When true, the entry size is fixed; otherwise, false.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
        /// <returns>The dictionary read from the buffer, or null.</returns>
        public Dictionary<TKey, TValue>? DecodeTaggedDictionary<TKey, TValue>(
            int tag,
            int minKeySize,
            int minValueSize,
            bool fixedSize,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
        {
            if (DecodeTaggedParamHeader(tag,
                    fixedSize ? EncodingDefinitions.TagFormat.VSize : EncodingDefinitions.TagFormat.FSize))
            {
                SkipSize(fixedLength: !fixedSize);
                return DecodeDictionary(minKeySize, minValueSize, keyDecodeFunc, valueDecodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged dictionary from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minKeySize">The minimum size of each key, in bytes.</param>
        /// <param name="withBitSequence">When true, null dictionary values are encoded using a bit sequence.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary read from the buffer, or null.</returns>
        public Dictionary<TKey, TValue?>? DecodeTaggedDictionary<TKey, TValue>(
            int tag,
            int minKeySize,
            bool withBitSequence,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : class
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipSize(fixedLength: true);
                return DecodeDictionary(minKeySize, withBitSequence, keyDecodeFunc, valueDecodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged dictionary from the buffer. The dictionary's value type is a nullable value type.
        /// </summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minKeySize">The minimum size of each key, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary read from the buffer, or null.</returns>
        public Dictionary<TKey, TValue?>? DecodeTaggedDictionary<TKey, TValue>(
            int tag,
            int minKeySize,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : struct
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipSize(fixedLength: true);
                return DecodeDictionary(minKeySize, keyDecodeFunc, valueDecodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged sequence from the buffer. The element type can be nullable only if it corresponds to
        /// a proxy class or mapped Slice class.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minElementSize">The minimum size of each element, in bytes.</param>
        /// <param name="fixedSize">True when the element size is fixed; otherwise, false.</param>
        /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
        /// <returns>The sequence read from the buffer as an ICollection{T}, or null.</returns>
        public ICollection<T>? DecodeTaggedSequence<T>(
            int tag,
            int minElementSize,
            bool fixedSize,
            IceDecodeFunc<T> decodeFunc)
        {
            if (DecodeTaggedParamHeader(tag,
                    fixedSize ? EncodingDefinitions.TagFormat.VSize : EncodingDefinitions.TagFormat.FSize))
            {
                if (!fixedSize || minElementSize > 1) // the size is optimized out for a fixed element size of 1
                {
                    SkipSize(fixedLength: !fixedSize);
                }
                return DecodeSequence(minElementSize, decodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged sequence of nullable elements from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="withBitSequence">True when null elements are encoded using a bit sequence; otherwise, false.
        /// </param>
        /// <param name="decodeFunc">The decode function for each non-null element of the sequence.</param>
        /// <returns>The sequence read from the buffer as an ICollection{T?}, or null.</returns>
        public ICollection<T?>? DecodeTaggedSequence<T>(int tag, bool withBitSequence, IceDecodeFunc<T> decodeFunc)
            where T : class
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipSize(fixedLength: true);
                return DecodeSequence(withBitSequence, decodeFunc);
            }
            else
            {
                return null;
            }
        }

        /// <summary>Decodes a tagged sequence of nullable values from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="decodeFunc">The decode function for each non-null value of the sequence.</param>
        /// <returns>The sequence read from the buffer as an ICollection{T?}, or null.</returns>
        public ICollection<T?>? DecodeTaggedSequence<T>(int tag, IceDecodeFunc<T> decodeFunc)
            where T : struct
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipSize(fixedLength: true);
                return DecodeSequence(decodeFunc);
            }
            else
            {
                return null;
            }
        }

        /// <summary>Decodes a tagged sorted dictionary from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minKeySize">The minimum size of each key, in bytes.</param>
        /// <param name="minValueSize">The minimum size of each value, in bytes.</param>
        /// <param name="fixedSize">True when the entry size is fixed; otherwise, false.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
        /// <returns>The sorted dictionary read from the buffer, or null.</returns>
        public SortedDictionary<TKey, TValue>? DecodeTaggedSortedDictionary<TKey, TValue>(
            int tag,
            int minKeySize,
            int minValueSize,
            bool fixedSize,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc) where TKey : notnull
        {
            if (DecodeTaggedParamHeader(tag,
                    fixedSize ? EncodingDefinitions.TagFormat.VSize : EncodingDefinitions.TagFormat.FSize))
            {
                SkipSize(fixedLength: !fixedSize);
                return DecodeSortedDictionary(minKeySize, minValueSize, keyDecodeFunc, valueDecodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged sorted dictionary from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minKeySize">The minimum size of each key, in bytes.</param>
        /// <param name="withBitSequence">When true, null dictionary values are encoded using a bit sequence.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary read from the buffer, or null.</returns>
        public SortedDictionary<TKey, TValue?>? DecodeTaggedSortedDictionary<TKey, TValue>(
            int tag,
            int minKeySize,
            bool withBitSequence,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : class
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipSize(fixedLength: true);
                return DecodeSortedDictionary(minKeySize, withBitSequence, keyDecodeFunc, valueDecodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged sorted dictionary from the buffer. The dictionary's value type is a nullable value
        /// type.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minKeySize">The minimum size of each key, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary read from the buffer, or null.</returns>
        public SortedDictionary<TKey, TValue?>? DecodeTaggedSortedDictionary<TKey, TValue>(
            int tag,
            int minKeySize,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : struct
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipSize(fixedLength: true);
                return DecodeSortedDictionary(minKeySize, keyDecodeFunc, valueDecodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged struct from the buffer.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="fixedSize">True when the struct has a fixed size on the wire; otherwise, false.</param>
        /// <param name="decodeFunc">The decode function used to create and read the struct.</param>
        /// <returns>The struct T read from the buffer, or null.</returns>
        public T? DecodeTaggedStruct<T>(int tag, bool fixedSize, IceDecodeFunc<T> decodeFunc) where T : struct
        {
            if (DecodeTaggedParamHeader(tag,
                    fixedSize ? EncodingDefinitions.TagFormat.VSize : EncodingDefinitions.TagFormat.FSize))
            {
                SkipSize(fixedLength: !fixedSize);
                return decodeFunc(this);
            }
            return null;
        }

        // Other methods

        /// <summary>Decodes a bit sequence from the buffer.</summary>
        /// <param name="bitSequenceSize">The minimum number of bits in the sequence.</param>
        /// <returns>The read-only bit sequence read from the buffer.</returns>
        public ReadOnlyBitSequence DecodeBitSequence(int bitSequenceSize)
        {
            int size = (bitSequenceSize >> 3) + ((bitSequenceSize & 0x07) != 0 ? 1 : 0);
            int startPos = Pos;
            Pos += size;
            return new ReadOnlyBitSequence(_buffer.Span.Slice(startPos, size));
        }

        /// <summary>Constructs a new Ice decoder over a byte buffer.</summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="encoding">The encoding of the buffer.</param>
        /// <param name="connection">The connection (optional).</param>
        /// <param name="invoker">The invoker.</param>
        /// <param name="typeIdClassFactories">Optional dictionary used to map Slice type Ids to classes, if null
        /// <see cref="Runtime.TypeIdClassFactoryDictionary"/> will be used.</param>
        /// <param name="typeIdExceptionFactories">Optional dictionary used to map Slice type Ids to exceptions, if
        /// null <see cref="Runtime.TypeIdRemoteExceptionFactoryDictionary"/> will be used.</param>
        /// <param name="compactTypeIdClassFactories">Optional dictionary used to map Slice compact type Ids to
        /// classes, if null <see cref="Runtime.CompactTypeIdClassFactoryDictionary"/> will be used.</param>
        internal IceDecoder(
            ReadOnlyMemory<byte> buffer,
            Encoding encoding,
            Connection? connection = null,
            IInvoker? invoker = null,
            IReadOnlyDictionary<string, Lazy<ClassFactory>>? typeIdClassFactories = null,
            IReadOnlyDictionary<string, Lazy<RemoteExceptionFactory>>? typeIdExceptionFactories = null,
            IReadOnlyDictionary<int, Lazy<ClassFactory>>? compactTypeIdClassFactories = null)
        {
            Connection = connection;
            Invoker = invoker;

            _classGraphMaxDepth = connection?.ClassGraphMaxDepth ?? 100;

            Pos = 0;
            _buffer = buffer;
            Encoding = encoding;
            Encoding.CheckSupported();

            _typeIdClassFactories = typeIdClassFactories;
            _typeIdRemoteExceptionFactories = typeIdExceptionFactories;
            _compactTypeIdClassFactories = compactTypeIdClassFactories;
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

        /// <summary>Decodes an endpoint from the buffer. Only called when the Ice decoder uses the 1.1 encoding.
        /// </summary>
        /// <param name="protocol">The Ice protocol of this endpoint.</param>
        /// <returns>The endpoint read from the buffer.</returns>
        internal Endpoint DecodeEndpoint11(Protocol protocol)
        {
            Debug.Assert(OldEncoding);

            Endpoint endpoint;

            Transport transport = this.DecodeTransport();

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

            var encoding = new Encoding(this);

            IIce1EndpointFactory? ice1EndpointFactory = null;
            if (protocol == Protocol.Ice1 &&
                encoding.IsSupported &&
                TransportRegistry.TryGetValue(transport, out IEndpointFactory? factory))
            {
                ice1EndpointFactory = factory as IIce1EndpointFactory;
            }

            // We need to read the encapsulation except for ice1 + null factory.
            if (protocol == Protocol.Ice1 && ice1EndpointFactory == null)
            {
                endpoint = OpaqueEndpoint.Create(transport,
                                                 encoding,
                                                 _buffer.Slice(Pos, size));
                Pos += size;
            }
            else if (encoding == Encoding.V11) // i.e. all in same encoding
            {
                int oldPos = Pos;
                if (protocol == Protocol.Ice1)
                {
                    Debug.Assert(ice1EndpointFactory != null); // see if block above with OpaqueEndpoint creation
                    endpoint = ice1EndpointFactory.CreateIce1Endpoint(this);
                }
                else
                {
                    var data = new EndpointData(transport,
                                                host: DecodeString(),
                                                port: DecodeUShort(),
                                                options: DecodeArray(1, BasicIceDecodeFuncs.StringIceDecodeFunc));

                    endpoint = data.ToEndpoint(protocol);
                }

                // Make sure we read the full encapsulation.
                if (Pos != oldPos + size)
                {
                    throw new InvalidDataException($"{oldPos + size - Pos} bytes left in endpoint encapsulation");
                }
            }
            else
            {
                string transportName = transport.ToString().ToLowerInvariant();
                throw new InvalidDataException(
                    @$"cannot read endpoint for protocol '{protocol.GetName()}' and transport '{transportName
                    }' with endpoint encapsulation encoded with encoding '{encoding}'");
            }

            return endpoint;
        }

        /// <summary>Decodes a field from the buffer.</summary>
        /// <returns>The key and value of the field. The read-only memory for the value is backed by the buffer, the
        /// data is not copied.</returns>
        internal (int Key, ReadOnlyMemory<byte> Value) DecodeField()
        {
            int key = DecodeVarInt();
            int entrySize = DecodeSize();
            ReadOnlyMemory<byte> value = _buffer.Slice(Pos, entrySize);
            Pos += entrySize;
            return (key, value);
        }

        /// <summary>Checks if the buffer holds a tagged proxy for the given tag, and when it does, skips the size
        /// of this proxy.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>True when the next bytes on the buffer correspond to the proxy; otherwise, false.</returns>
        internal bool DecodeTaggedProxyHeader(int tag)
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipSize(fixedLength: true);
                return true;
            }
            else
            {
                return false;
            }
        }

        internal void Skip(int size)
        {
            if (size < 0 || size > _buffer.Length - Pos)
            {
                throw new IndexOutOfRangeException($"cannot skip {size} bytes");
            }
            Pos += size;
        }

        /// <summary>Decodes a sequence size and makes sure there is enough space in the underlying buffer to read the
        /// sequence. This validation is performed to make sure we do not allocate a large container based on an
        /// invalid encoded size.</summary>
        /// <param name="minElementSize">The minimum encoded size of an element of the sequence, in bytes. This value is
        /// 0 for sequence of nullable types other than mapped Slice classes and proxies.</param>
        /// <returns>The number of elements in the sequence.</returns>
        private int DecodeAndCheckSeqSize(int minElementSize)
        {
            int sz = DecodeSize();

            if (sz == 0)
            {
                return 0;
            }

            // When minElementSize is 0, we only count of bytes that hold the bit sequence.
            int minSize = minElementSize > 0 ? sz * minElementSize : (sz >> 3) + ((sz & 0x07) != 0 ? 1 : 0);

            // With _minTotalSeqSize, we make sure that multiple sequences within a buffer can't trigger maliciously
            // the allocation of a large amount of memory before we read these sequences from the buffer.
            _minTotalSeqSize += minSize;

            if (Pos + minSize > _buffer.Length || _minTotalSeqSize > _buffer.Length)
            {
                throw new InvalidDataException("invalid sequence size");
            }
            return sz;
        }

        private ReadOnlyMemory<byte> DecodeBitSequenceMemory(int bitSequenceSize)
        {
            int size = (bitSequenceSize >> 3) + ((bitSequenceSize & 0x07) != 0 ? 1 : 0);
            int startPos = Pos;
            Pos += size;
            return _buffer.Slice(startPos, size);
        }

        private TDict DecodeDictionary<TDict, TKey, TValue>(
            TDict dict,
            int size,
            bool withBitSequence,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc)
            where TDict : IDictionary<TKey, TValue?>
            where TKey : notnull
            where TValue : class
        {
            if (withBitSequence)
            {
                ReadOnlyBitSequence bitSequence = DecodeBitSequence(size);
                for (int i = 0; i < size; ++i)
                {
                    TKey key = keyDecodeFunc(this);
                    TValue? value = bitSequence[i] ? valueDecodeFunc(this) : (TValue?)null;
                    dict.Add(key, value);
                }
            }
            else
            {
                for (int i = 0; i < size; ++i)
                {
                    TKey key = keyDecodeFunc(this);
                    TValue value = valueDecodeFunc(this);
                    dict.Add(key, value);
                }
            }
            return dict;
        }

        private TDict DecodeDictionary<TDict, TKey, TValue>(
            TDict dict,
            int size,
            IceDecodeFunc<TKey> keyDecodeFunc,
            IceDecodeFunc<TValue> valueDecodeFunc)
            where TDict : IDictionary<TKey, TValue?>
            where TKey : notnull
            where TValue : struct
        {
            ReadOnlyBitSequence bitSequence = DecodeBitSequence(size);
            for (int i = 0; i < size; ++i)
            {
                TKey key = keyDecodeFunc(this);
                TValue? value = bitSequence[i] ? valueDecodeFunc(this) : (TValue?)null;
                dict.Add(key, value);
            }
            return dict;
        }

        private int DecodeSize11()
        {
            byte b = DecodeByte();
            if (b < 255)
            {
                return b;
            }

            int size = DecodeInt();
            if (size < 0)
            {
                throw new InvalidDataException($"read invalid size: {size}");
            }
            return size;
        }

        private int DecodeSize20()
        {
            checked
            {
                return (int)DecodeVarULong();
            }
        }

        private int ReadSpan(Span<byte> span)
        {
            int length = Math.Min(span.Length, _buffer.Length - Pos);
            _buffer.Span.Slice(Pos, length).CopyTo(span);
            Pos += length;
            return length;
        }

        /// <summary>Determines if a tagged parameter or data member is available for reading.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="expectedFormat">The expected format of the tagged parameter.</param>
        /// <returns>True if the tagged parameter is present; otherwise, false.</returns>
        private bool DecodeTaggedParamHeader(int tag, EncodingDefinitions.TagFormat expectedFormat)
        {
            // The current slice has no tagged parameter.
            if (_current.InstanceType != InstanceType.None &&
                (_current.SliceFlags & EncodingDefinitions.SliceFlags.HasTaggedMembers) == 0)
            {
                return false;
            }

            int requestedTag = tag;

            while (true)
            {
                if (_buffer.Length - Pos <= 0)
                {
                    return false; // End of buffer also indicates end of tagged parameters.
                }

                int savedPos = Pos;

                int v = DecodeByte();
                if (v == EncodingDefinitions.TaggedEndMarker)
                {
                    Pos = savedPos; // rewind
                    return false;
                }

                var format = (EncodingDefinitions.TagFormat)(v & 0x07); // First 3 bits.
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
                    SkipTagged(format);
                }
                else
                {
                    // When expected format is VInt, format can be any of F1 through F8. Note that the exact format
                    // received does not matter in this case.
                    if (format != expectedFormat && (expectedFormat != EncodingDefinitions.TagFormat.VInt ||
                            (int)format > (int)EncodingDefinitions.TagFormat.F8))
                    {
                        throw new InvalidDataException($"invalid tagged parameter '{tag}': unexpected format");
                    }
                    return true;
                }
            }
        }

        /// <summary>Skips over a size value.</summary>
        /// <param name="fixedLength">When true and the encoding is 1.1, it's a fixed length size encoded on 4 bytes.
        /// When false, or the encoding is not 1.1, it's a variable-length size.</param>
        private void SkipSize(bool fixedLength = false)
        {
            if (OldEncoding)
            {
                if (fixedLength)
                {
                    Skip(4);
                }
                else
                {
                    byte b = DecodeByte();
                    if (b == 255)
                    {
                        Skip(4);
                    }
                }
            }
            else
            {
                Skip(_buffer.Span[Pos].DecodeSizeLength20());
            }
        }

        private void SkipTagged(EncodingDefinitions.TagFormat format)
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
                    if (OldEncoding)
                    {
                        int size = DecodeInt();
                        if (size < 0)
                        {
                            throw new InvalidDataException("invalid negative fixed-length size");
                        }
                        Skip(size);
                    }
                    else
                    {
                        Skip(DecodeSize20());
                    }
                    break;
                default:
                    throw new InvalidDataException(
                        $"cannot skip tagged parameter or data member with tag format '{format}'");
            }
        }

        private void SkipTaggedParams()
        {
            while (true)
            {
                if (_buffer.Length - Pos <= 0)
                {
                    break;
                }

                int v = DecodeByte();
                if (v == EncodingDefinitions.TaggedEndMarker)
                {
                    break;
                }

                var format = (EncodingDefinitions.TagFormat)(v & 0x07); // Read first 3 bits.
                if ((v >> 3) == 30)
                {
                    SkipSize();
                }
                SkipTagged(format);
            }
        }

        // Helper base class for the concrete collection implementations.
        private abstract class CollectionBase<T> : ICollection<T>
        {
            public struct Enumerator : IEnumerator<T>
            {
                public T Current
                {
                    get
                    {
                        if (_pos == 0 || _pos > _collection.Count)
                        {
                            throw new InvalidOperationException();
                        }
                        return _current;
                    }

                    private set => _current = value;
                }

                object? IEnumerator.Current => Current;

                private readonly CollectionBase<T> _collection;
                private T _current;
                private int _pos;

                public void Dispose()
                {
                }

                public bool MoveNext()
                {
                    if (_pos < _collection.Count)
                    {
                        Current = _collection.Decode(_pos);
                        _pos++;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }

                public void Reset() => throw new NotImplementedException();

                // Disable these warnings as the _current field is never read before it is initialized in MoveNext.
                // Declaring this field as nullable is not an option for a generic T that can be used with reference
                // and value types.
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
                internal Enumerator(CollectionBase<T> collection)
#pragma warning restore CS8618
                {
                    _collection = collection;
#pragma warning disable CS8601 // Possible null reference assignment.
                    _current = default;
#pragma warning restore CS8601
                    _pos = 0;
                }
            }

            public int Count { get; }
            public bool IsReadOnly => true;
            protected IceDecoder IceDecoder { get; }

            private bool _enumeratorRetrieved;

            public void Add(T item) => throw new NotSupportedException();
            public void Clear() => throw new NotSupportedException();
            public bool Contains(T item) => throw new NotSupportedException();

            public void CopyTo(T[] array, int arrayIndex)
            {
                foreach (T value in this)
                {
                    array[arrayIndex++] = value;
                }
            }
            public IEnumerator<T> GetEnumerator()
            {
                if (_enumeratorRetrieved)
                {
                    throw new NotSupportedException("cannot get a second enumerator for this enumerable");
                }
                _enumeratorRetrieved = true;
                return new Enumerator(this);
            }

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public bool Remove(T item) => throw new NotSupportedException();
            public void Reset() => throw new NotSupportedException();

            private protected abstract T Decode(int pos);

            protected CollectionBase(IceDecoder iceDecoder, int minElementSize)
            {
                Count = iceDecoder.DecodeAndCheckSeqSize(minElementSize);
                IceDecoder = iceDecoder;
            }
        }

        // Collection<T> holds the size of a Slice sequence and reads the sequence elements from the Inputbuffer
        // on-demand. It does not fully implement IEnumerable<T> and ICollection<T> (i.e. some methods throw
        // NotSupportedException) because it's not resettable: you can't use it to unmarshal the same bytes multiple
        // times.
        private sealed class Collection<T> : CollectionBase<T>
        {
            private readonly IceDecodeFunc<T> _decodeFunc;
            internal Collection(IceDecoder iceDecoder, int minElementSize, IceDecodeFunc<T> decodeFunc)
                : base(iceDecoder, minElementSize) => _decodeFunc = decodeFunc;

            private protected override T Decode(int pos)
            {
                Debug.Assert(pos < Count);
                return _decodeFunc(IceDecoder);
            }
        }

        // Similar to Collection<T>, except we are reading a sequence<T?> where T is a reference type. T here must not
        // correspond to a mapped Slice class or to a proxy class.
        private sealed class NullableCollection<T> : CollectionBase<T?> where T : class
        {
            private readonly ReadOnlyMemory<byte> _bitSequenceMemory;
            readonly IceDecodeFunc<T> _decodeFunc;

            internal NullableCollection(IceDecoder iceDecoder, IceDecodeFunc<T> decodeFunc)
                : base(iceDecoder, 0)
            {
                _bitSequenceMemory = iceDecoder.DecodeBitSequenceMemory(Count);
                _decodeFunc = decodeFunc;
            }

            private protected override T? Decode(int pos)
            {
                Debug.Assert(pos < Count);
                var bitSequence = new ReadOnlyBitSequence(_bitSequenceMemory.Span);
                return bitSequence[pos] ? _decodeFunc(IceDecoder) : null;
            }
        }

        // Similar to Collection<T>, except we are reading a sequence<T?> where T is a value type.
        private sealed class NullableValueCollection<T> : CollectionBase<T?> where T : struct
        {
            private readonly ReadOnlyMemory<byte> _bitSequenceMemory;
            private readonly IceDecodeFunc<T> _decodeFunc;

            internal NullableValueCollection(IceDecoder iceDecoder, IceDecodeFunc<T> decodeFunc)
                : base(iceDecoder, 0)
            {
                _bitSequenceMemory = iceDecoder.DecodeBitSequenceMemory(Count);
                _decodeFunc = decodeFunc;
            }

            private protected override T? Decode(int pos)
            {
                Debug.Assert(pos < Count);
                var bitSequence = new ReadOnlyBitSequence(_bitSequenceMemory.Span);
                return bitSequence[pos] ? _decodeFunc(IceDecoder) : null;
            }
        }
    }
}
