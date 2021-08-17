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
    /// <summary>Decodes a byte buffer encoded using the Ice encoding.</summary>
    public abstract class IceDecoder
    {
        /// <summary>Connection used when decoding proxies.</summary>
        internal Connection? Connection { get; }

        /// <summary>Invoker used when decoding proxies.</summary>
        internal IInvoker? Invoker { get; }

        /// <summary>The 0-based position (index) in the underlying buffer.</summary>
        internal int Pos { get; private protected set; }

        /// <summary>The sliced-off slices held by the current instance, if any.</summary>
        internal abstract SlicedData? SlicedData { get; }

        // The byte buffer we are decoding.
        private protected readonly ReadOnlyMemory<byte> _buffer;

        // The sum of all the minimum sizes (in bytes) of the sequences decoded from this buffer. Must not exceed the
        // buffer size.
        private int _minTotalSeqSize;

        // Decode methods for basic types

        /// <summary>Decodes a bool.</summary>
        /// <returns>The bool decoded by this decoder.</returns>
        public bool DecodeBool() => _buffer.Span[Pos++] == 1;

        /// <summary>Decodes a byte.</summary>
        /// <returns>The byte decoded by this decoder.</returns>
        public byte DecodeByte() => _buffer.Span[Pos++];

        /// <summary>Decodes a double.</summary>
        /// <returns>The double decoded by this decoder.</returns>
        public double DecodeDouble()
        {
            double value = BitConverter.ToDouble(_buffer.Span.Slice(Pos, sizeof(double)));
            Pos += sizeof(double);
            return value;
        }

        /// <summary>Decodes a float.</summary>
        /// <returns>The float decoded by this decoder.</returns>
        public float DecodeFloat()
        {
            float value = BitConverter.ToSingle(_buffer.Span.Slice(Pos, sizeof(float)));
            Pos += sizeof(float);
            return value;
        }

        /// <summary>Decodes an int.</summary>
        /// <returns>The int decoded by this decoder.</returns>
        public int DecodeInt()
        {
            int value = BitConverter.ToInt32(_buffer.Span.Slice(Pos, sizeof(int)));
            Pos += sizeof(int);
            return value;
        }

        /// <summary>Decodes a long.</summary>
        /// <returns>The long decoded by this decoder.</returns>
        public long DecodeLong()
        {
            long value = BitConverter.ToInt64(_buffer.Span.Slice(Pos, sizeof(long)));
            Pos += sizeof(long);
            return value;
        }

        /// <summary>Decodes a short.</summary>
        /// <returns>The short decoded by this decoder.</returns>
        public short DecodeShort()
        {
            short value = BitConverter.ToInt16(_buffer.Span.Slice(Pos, sizeof(short)));
            Pos += sizeof(short);
            return value;
        }

        /// <summary>Decodes a size encoded on a variable number of bytes.</summary>
        /// <returns>The size decoded by this decoder.</returns>
        public abstract int DecodeSize();

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
                string value = _buffer.Slice(Pos, size).Span.DecodeString();
                Pos += size;
                return value;
            }
        }

        /// <summary>Decodes a uint.</summary>
        /// <returns>The uint decoded by this decoder.</returns>
        public uint DecodeUInt()
        {
            uint value = BitConverter.ToUInt32(_buffer.Span.Slice(Pos, sizeof(uint)));
            Pos += sizeof(uint);
            return value;
        }

        /// <summary>Decodes a ulong.</summary>
        /// <returns>The ulong decoded by this decoder.</returns>
        public ulong DecodeULong()
        {
            ulong value = BitConverter.ToUInt64(_buffer.Span.Slice(Pos, sizeof(ulong)));
            Pos += sizeof(ulong);
            return value;
        }

        /// <summary>Decodes a ushort.</summary>
        /// <returns>The ushort decoded by this decoder.</returns>
        public ushort DecodeUShort()
        {
            ushort value = BitConverter.ToUInt16(_buffer.Span.Slice(Pos, sizeof(ushort)));
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
            (_buffer.Span[Pos] & 0x03) switch
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
            (_buffer.Span[Pos] & 0x03) switch
            {
                0 => (uint)DecodeByte() >> 2,   // cast to uint to use operator >> for uint instead of int, which is
                1 => (uint)DecodeUShort() >> 2, // later implicitly converted to ulong
                2 => DecodeUInt() >> 2,
                _ => DecodeULong() >> 2
            };

        // Decode methods for constructed types

        /// <summary>Decodes a sequence of fixed-size numeric values and returns an array.</summary>
        /// <returns>The sequence decoded by this decoder, as an array.</returns>
        public T[] DecodeArray<T>() where T : struct
        {
            int elementSize = Unsafe.SizeOf<T>();
            var value = new T[DecodeAndCheckSeqSize(elementSize)];
            int byteCount = elementSize * value.Length;
            _buffer.Span.Slice(Pos, byteCount).CopyTo(MemoryMarshal.Cast<T, byte>(value));
            Pos += byteCount;
            return value;
        }

        /// <summary>Decodes a sequence of fixed-size numeric values and returns an array.</summary>
        /// <param name="checkElement">A delegate use to checks each element of the array.</param>
        /// <returns>The sequence decoded by this decoder, as an array.</returns>
        public T[] DecodeArray<T>(Action<T> checkElement) where T : struct
        {
            T[] value = DecodeArray<T>();
            foreach (T e in value)
            {
                checkElement(e);
            }
            return value;
        }

        /// <summary>Decodes a sequence and returns an array.</summary>
        /// <param name="minElementSize">The minimum size of each element of the sequence, in bytes.</param>
        /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
        /// <returns>The sequence decoded by this decoder, as an array.</returns>
        public T[] DecodeArray<T>(int minElementSize, DecodeFunc<T> decodeFunc) =>
            DecodeSequence(minElementSize, decodeFunc).ToArray();

        /// <summary>Decodes a sequence of nullable elements and returns an array.</summary>
        /// <param name="withBitSequence">True when null elements are encoded using a bit sequence; otherwise, false.
        /// </param>
        /// <param name="decodeFunc">The decode function for each non-null element of the sequence.</param>
        /// <returns>The sequence decoded by this decoder, as an array.</returns>
        public T?[] DecodeArray<T>(bool withBitSequence, DecodeFunc<T> decodeFunc) where T : class =>
            DecodeSequence(withBitSequence, decodeFunc).ToArray();

        /// <summary>Decodes a sequence of nullable values and returns an array.</summary>
        /// <param name="decodeFunc">The decode function for each non-null element of the sequence.</param>
        /// <returns>The sequence decoded by this decoder, as an array.</returns>
        public T?[] DecodeArray<T>(DecodeFunc<T> decodeFunc) where T : struct => DecodeSequence(decodeFunc).ToArray();

        /// <summary>Decodes a class instance.</summary>
        /// <returns>The decoded class instance.</returns>
        public abstract T DecodeClass<T>() where T : AnyClass;

        /// <summary>Decodes a dictionary.</summary>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="minValueSize">The minimum size of each value of the dictionary, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
        /// <returns>The dictionary decoded by this decoder.</returns>
        public Dictionary<TKey, TValue> DecodeDictionary<TKey, TValue>(
            int minKeySize,
            int minValueSize,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
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

        /// <summary>Decodes a dictionary.</summary>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="withBitSequence">When true, null dictionary values are encoded using a bit sequence.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary decoded by this decoder.</returns>
        public Dictionary<TKey, TValue?> DecodeDictionary<TKey, TValue>(
            int minKeySize,
            bool withBitSequence,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : class
        {
            int sz = DecodeAndCheckSeqSize(minKeySize);
            return DecodeDictionary(new Dictionary<TKey, TValue?>(sz), sz, withBitSequence, keyDecodeFunc, valueDecodeFunc);
        }

        /// <summary>Decodes a dictionary.</summary>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary decoded by this decoder.</returns>
        public Dictionary<TKey, TValue?> DecodeDictionary<TKey, TValue>(
            int minKeySize,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : struct
        {
            int sz = DecodeAndCheckSeqSize(minKeySize);
            return DecodeDictionary(new Dictionary<TKey, TValue?>(sz), sz, keyDecodeFunc, valueDecodeFunc);
        }

        /// <summary>Decodes a remote exception.</summary>
        /// <returns>The remote exception.</returns>
        public abstract RemoteException DecodeException();

        /// <summary>Decodes a nullable class instance.</summary>
        /// <returns>The class instance, or null.</returns>
        public abstract T? DecodeNullableClass<T>() where T : AnyClass;

        /// <summary>Decodes a nullable proxy.</summary>
        /// <returns>The decoded proxy, or null.</returns>
        public abstract Proxy? DecodeNullableProxy();

        /// <summary>Decodes a proxy.</summary>
        /// <returns>The decoded proxy</returns>
        public Proxy DecodeProxy() =>
            DecodeNullableProxy() ?? throw new InvalidDataException("decoded null for a non-nullable proxy");

        /// <summary>Decodes a sequence.</summary>
        /// <param name="minElementSize">The minimum size of each element of the sequence, in bytes.</param>
        /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
        /// <returns>A collection that provides the size of the sequence and allows you to decode the sequence from the
        /// the buffer. The return value does not fully implement ICollection{T}, in particular you can only call
        /// GetEnumerator() once on this collection. You would typically use this collection to construct a List{T} or
        /// some other generic collection that can be constructed from an IEnumerable{T}.</returns>
        public ICollection<T> DecodeSequence<T>(int minElementSize, DecodeFunc<T> decodeFunc) =>
            new Collection<T>(this, minElementSize, decodeFunc);

        /// <summary>Decodes a sequence of nullable elements. The element type is a reference type.
        /// </summary>
        /// <param name="withBitSequence">True when null elements are encoded using a bit sequence; otherwise, false.
        /// </param>
        /// <param name="decodeFunc">The decode function for each non-null element of the sequence.</param>
        /// <returns>A collection that provides the size of the sequence and allows you to decode the sequence from the
        /// the buffer. The returned collection does not fully implement ICollection{T?}, in particular you can only
        /// call GetEnumerator() once on this collection. You would typically use this collection to construct a
        /// List{T?} or some other generic collection that can be constructed from an IEnumerable{T?}.</returns>
        public ICollection<T?> DecodeSequence<T>(bool withBitSequence, DecodeFunc<T> decodeFunc) where T : class =>
            withBitSequence ? new NullableCollection<T>(this, decodeFunc) : (ICollection<T?>)DecodeSequence(1, decodeFunc);

        /// <summary>Decodes a sequence of nullable values.</summary>
        /// <param name="decodeFunc">The decode function for each non-null element (value) of the sequence.
        /// </param>
        /// <returns>A collection that provides the size of the sequence and allows you to decode the sequence from the
        /// the buffer. The returned collection does not fully implement ICollection{T?}, in particular you can only
        /// call GetEnumerator() once on this collection. You would typically use this collection to construct a
        /// List{T?} or some other generic collection that can be constructed from an IEnumerable{T?}.</returns>
        public ICollection<T?> DecodeSequence<T>(DecodeFunc<T> decodeFunc) where T : struct =>
            new NullableValueCollection<T>(this, decodeFunc);

        /// <summary>Decodes a sorted dictionary.</summary>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="minValueSize">The minimum size of each value of the dictionary, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
        /// <returns>The sorted dictionary decoded by this decoder.</returns>
        public SortedDictionary<TKey, TValue> DecodeSortedDictionary<TKey, TValue>(
            int minKeySize,
            int minValueSize,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
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

        /// <summary>Decodes a sorted dictionary.</summary>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="withBitSequence">When true, null dictionary values are encoded using a bit sequence.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.
        /// </param>
        /// <returns>The sorted dictionary decoded by this decoder.</returns>
        public SortedDictionary<TKey, TValue?> DecodeSortedDictionary<TKey, TValue>(
            int minKeySize,
            bool withBitSequence,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : class =>
            DecodeDictionary(
                new SortedDictionary<TKey, TValue?>(),
                DecodeAndCheckSeqSize(minKeySize),
                withBitSequence,
                keyDecodeFunc,
                valueDecodeFunc);

        /// <summary>Decodes a sorted dictionary. The dictionary's value type is a nullable value type.
        /// </summary>
        /// <param name="minKeySize">The minimum size of each key of the dictionary, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The sorted dictionary decoded by this decoder.</returns>
        public SortedDictionary<TKey, TValue?> DecodeSortedDictionary<TKey, TValue>(
            int minKeySize,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : struct =>
            DecodeDictionary(
                new SortedDictionary<TKey, TValue?>(),
                DecodeAndCheckSeqSize(minKeySize),
                keyDecodeFunc,
                valueDecodeFunc);

        // Decode methods for tagged basic types

        /// <summary>Decodes a tagged bool.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The bool decoded by this decoder, or null.</returns>
        public bool? DecodeTaggedBool(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F1) ? DecodeBool() : (bool?)null;

        /// <summary>Decodes a tagged byte.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The byte decoded by this decoder, or null.</returns>
        public byte? DecodeTaggedByte(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F1) ? DecodeByte() : (byte?)null;

        /// <summary>Decodes a tagged double.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The double decoded by this decoder, or null.</returns>
        public double? DecodeTaggedDouble(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F8) ? DecodeDouble() : (double?)null;

        /// <summary>Decodes a tagged float.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The float decoded by this decoder, or null.</returns>
        public float? DecodeTaggedFloat(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F4) ? DecodeFloat() : (float?)null;

        /// <summary>Decodes a tagged int.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The int decoded by this decoder, or null.</returns>
        public int? DecodeTaggedInt(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F4) ? DecodeInt() : (int?)null;

        /// <summary>Decodes a tagged long.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The long decoded by this decoder, or null.</returns>
        public long? DecodeTaggedLong(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F8) ? DecodeLong() : (long?)null;

        /// <summary>Decodes a tagged short.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The short decoded by this decoder, or null.</returns>
        public short? DecodeTaggedShort(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F2) ? DecodeShort() : (short?)null;

        /// <summary>Decodes a tagged size.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The size decoded by this decoder, or null.</returns>
        public int? DecodeTaggedSize(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.Size) ? DecodeSize() : (int?)null;

        /// <summary>Decodes a tagged string.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The string decoded by this decoder, or null.</returns>
        public string? DecodeTaggedString(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VSize) ? DecodeString() : null;

        /// <summary>Decodes a tagged uint.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The uint decoded by this decoder, or null.</returns>
        public uint? DecodeTaggedUInt(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F4) ? DecodeUInt() : (uint?)null;

        /// <summary>Decodes a tagged ulong.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The ulong decoded by this decoder, or null.</returns>
        public ulong? DecodeTaggedULong(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F8) ? DecodeULong() : (ulong?)null;

        /// <summary>Decodes a tagged ushort.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The ushort decoded by this decoder, or null.</returns>
        public ushort? DecodeTaggedUShort(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.F2) ? DecodeUShort() : (ushort?)null;

        /// <summary>Decodes a tagged varint.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The int decoded by this decoder, or null.</returns>
        public int? DecodeTaggedVarInt(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VInt) ? DecodeVarInt() : (int?)null;

        /// <summary>Decodes a tagged varlong.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The long decoded by this decoder, or null.</returns>
        public long? DecodeTaggedVarLong(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VInt) ? DecodeVarLong() : (long?)null;

        /// <summary>Decodes a tagged varuint.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The uint decoded by this decoder, or null.</returns>
        public uint? DecodeTaggedVarUInt(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VInt) ? DecodeVarUInt() : (uint?)null;

        /// <summary>Decodes a tagged varulong.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The ulong decoded by this decoder, or null.</returns>
        public ulong? DecodeTaggedVarULong(int tag) =>
            DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.VInt) ? DecodeVarULong() : (ulong?)null;

        // Decode methods for tagged constructed types except class

        /// <summary>Decodes a tagged array of a fixed-size numeric type.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The sequence decoded by this decoder as an array, or null.</returns>
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

        /// <summary>Decodes a tagged array of a fixed-size numeric type.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="checkElement">A delegate use to checks each element of the array.</param>
        /// <returns>The sequence decoded by this decoder as an array, or null.</returns>
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

        /// <summary>Decodes a tagged array. The element type can be nullable only if it corresponds to
        /// a proxy class or mapped Slice class.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minElementSize">The minimum size of each element, in bytes.</param>
        /// <param name="fixedSize">True when the element size is fixed; otherwise, false.</param>
        /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
        /// <returns>The sequence decoded by this decoder as an array, or null.</returns>
        public T[]? DecodeTaggedArray<T>(int tag, int minElementSize, bool fixedSize, DecodeFunc<T> decodeFunc) =>
            DecodeTaggedSequence(tag, minElementSize, fixedSize, decodeFunc)?.ToArray();

        /// <summary>Decodes a tagged array of nullable elements.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="withBitSequence">True when null elements are encoded using a bit sequence; otherwise, false.
        /// </param>
        /// <param name="decodeFunc">The decode function for each non-null element of the array.</param>
        /// <returns>The array decoded by this decoder, or null.</returns>
        public T?[]? DecodeTaggedArray<T>(int tag, bool withBitSequence, DecodeFunc<T> decodeFunc) where T : class =>
            DecodeTaggedSequence(tag, withBitSequence, decodeFunc)?.ToArray();

        /// <summary>Decodes a tagged array of nullable values.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="decodeFunc">The decode function for each non-null value of the array.</param>
        /// <returns>The array decoded by this decoder, or null.</returns>
        public T?[]? DecodeTaggedArray<T>(int tag, DecodeFunc<T> decodeFunc) where T : struct =>
            DecodeTaggedSequence(tag, decodeFunc)?.ToArray();

        /// <summary>Decodes a tagged dictionary.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minKeySize">The minimum size of each key, in bytes.</param>
        /// <param name="minValueSize">The minimum size of each value, in bytes.</param>
        /// <param name="fixedSize">When true, the entry size is fixed; otherwise, false.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
        /// <returns>The dictionary decoded by this decoder, or null.</returns>
        public Dictionary<TKey, TValue>? DecodeTaggedDictionary<TKey, TValue>(
            int tag,
            int minKeySize,
            int minValueSize,
            bool fixedSize,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
        {
            if (DecodeTaggedParamHeader(
                tag,
                fixedSize ? EncodingDefinitions.TagFormat.VSize : EncodingDefinitions.TagFormat.FSize))
            {
                if (fixedSize)
                {
                    SkipSize();
                }
                else
                {
                    SkipFixedLengthSize(); // the fixed length size is used for var-size elements.
                }
                return DecodeDictionary(minKeySize, minValueSize, keyDecodeFunc, valueDecodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged dictionary.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minKeySize">The minimum size of each key, in bytes.</param>
        /// <param name="withBitSequence">When true, null dictionary values are encoded using a bit sequence.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary decoded by this decoder, or null.</returns>
        public Dictionary<TKey, TValue?>? DecodeTaggedDictionary<TKey, TValue>(
            int tag,
            int minKeySize,
            bool withBitSequence,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : class
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipFixedLengthSize();
                return DecodeDictionary(minKeySize, withBitSequence, keyDecodeFunc, valueDecodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged dictionary. The dictionary's value type is a nullable value type.
        /// </summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minKeySize">The minimum size of each key, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary decoded by this decoder, or null.</returns>
        public Dictionary<TKey, TValue?>? DecodeTaggedDictionary<TKey, TValue>(
            int tag,
            int minKeySize,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : struct
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipFixedLengthSize();
                return DecodeDictionary(minKeySize, keyDecodeFunc, valueDecodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged proxy.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>The decoded proxy (can be null).</returns>
        public Proxy? DecodeTaggedProxy(int tag) => DecodeTaggedProxyHeader(tag) ? DecodeProxy() : null;

        /// <summary>Decodes a tagged sequence. The element type can be nullable only if it corresponds to
        /// a proxy class or mapped Slice class.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minElementSize">The minimum size of each element, in bytes.</param>
        /// <param name="fixedSize">True when the element size is fixed; otherwise, false.</param>
        /// <param name="decodeFunc">The decode function for each element of the sequence.</param>
        /// <returns>The sequence decoded by this decoder as an ICollection{T}, or null.</returns>
        public ICollection<T>? DecodeTaggedSequence<T>(
            int tag,
            int minElementSize,
            bool fixedSize,
            DecodeFunc<T> decodeFunc)
        {
            if (DecodeTaggedParamHeader(
                    tag,
                    fixedSize ? EncodingDefinitions.TagFormat.VSize : EncodingDefinitions.TagFormat.FSize))
            {
                if (!fixedSize || minElementSize > 1) // the size is optimized out for a fixed element size of 1
                {
                    if (fixedSize)
                    {
                        SkipSize();
                    }
                    else
                    {
                        SkipFixedLengthSize(); // the fixed length size is used for var-size elements.
                    }
                }
                return DecodeSequence(minElementSize, decodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged sequence of nullable elements.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="withBitSequence">True when null elements are encoded using a bit sequence; otherwise, false.
        /// </param>
        /// <param name="decodeFunc">The decode function for each non-null element of the sequence.</param>
        /// <returns>The sequence decoded by this decoder as an ICollection{T?}, or null.</returns>
        public ICollection<T?>? DecodeTaggedSequence<T>(int tag, bool withBitSequence, DecodeFunc<T> decodeFunc)
            where T : class
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipFixedLengthSize();
                return DecodeSequence(withBitSequence, decodeFunc);
            }
            else
            {
                return null;
            }
        }

        /// <summary>Decodes a tagged sequence of nullable values.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="decodeFunc">The decode function for each non-null value of the sequence.</param>
        /// <returns>The sequence decoded by this decoder as an ICollection{T?}, or null.</returns>
        public ICollection<T?>? DecodeTaggedSequence<T>(int tag, DecodeFunc<T> decodeFunc)
            where T : struct
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipFixedLengthSize();
                return DecodeSequence(decodeFunc);
            }
            else
            {
                return null;
            }
        }

        /// <summary>Decodes a tagged sorted dictionary.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minKeySize">The minimum size of each key, in bytes.</param>
        /// <param name="minValueSize">The minimum size of each value, in bytes.</param>
        /// <param name="fixedSize">True when the entry size is fixed; otherwise, false.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each value of the dictionary.</param>
        /// <returns>The sorted dictionary decoded by this decoder, or null.</returns>
        public SortedDictionary<TKey, TValue>? DecodeTaggedSortedDictionary<TKey, TValue>(
            int tag,
            int minKeySize,
            int minValueSize,
            bool fixedSize,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc) where TKey : notnull
        {
            if (DecodeTaggedParamHeader(
                    tag,
                    fixedSize ? EncodingDefinitions.TagFormat.VSize : EncodingDefinitions.TagFormat.FSize))
            {
                if (fixedSize)
                {
                    SkipSize();
                }
                else
                {
                    SkipFixedLengthSize(); // the fixed length size is used for var-size elements.
                }
                return DecodeSortedDictionary(minKeySize, minValueSize, keyDecodeFunc, valueDecodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged sorted dictionary.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minKeySize">The minimum size of each key, in bytes.</param>
        /// <param name="withBitSequence">When true, null dictionary values are encoded using a bit sequence.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary decoded by this decoder, or null.</returns>
        public SortedDictionary<TKey, TValue?>? DecodeTaggedSortedDictionary<TKey, TValue>(
            int tag,
            int minKeySize,
            bool withBitSequence,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : class
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipFixedLengthSize();
                return DecodeSortedDictionary(minKeySize, withBitSequence, keyDecodeFunc, valueDecodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged sorted dictionary. The dictionary's value type is a nullable value
        /// type.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="minKeySize">The minimum size of each key, in bytes.</param>
        /// <param name="keyDecodeFunc">The decode function for each key of the dictionary.</param>
        /// <param name="valueDecodeFunc">The decode function for each non-null value of the dictionary.</param>
        /// <returns>The dictionary decoded by this decoder, or null.</returns>
        public SortedDictionary<TKey, TValue?>? DecodeTaggedSortedDictionary<TKey, TValue>(
            int tag,
            int minKeySize,
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
            where TKey : notnull
            where TValue : struct
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipFixedLengthSize();
                return DecodeSortedDictionary(minKeySize, keyDecodeFunc, valueDecodeFunc);
            }
            return null;
        }

        /// <summary>Decodes a tagged struct.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="fixedSize">True when the struct has a fixed size on the wire; otherwise, false.</param>
        /// <param name="decodeFunc">The decode function used to create and decode the struct.</param>
        /// <returns>The struct T decoded by this decoder, or null.</returns>
        public T? DecodeTaggedStruct<T>(int tag, bool fixedSize, DecodeFunc<T> decodeFunc) where T : struct
        {
            if (DecodeTaggedParamHeader(
                tag,
                fixedSize ? EncodingDefinitions.TagFormat.VSize : EncodingDefinitions.TagFormat.FSize))
            {
                if (fixedSize)
                {
                    SkipSize();
                }
                else
                {
                    SkipFixedLengthSize(); // the fixed length size is used for var-size elements.
                }
                return decodeFunc(this);
            }
            return null;
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
            return new ReadOnlyBitSequence(_buffer.Span.Slice(startPos, size));
        }

        // Logically internal methods that are marked public because they are called by the generated code.

        /// <summary>Marks the end of the decoding of a derived exception slice.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public abstract void IceEndDerivedExceptionSlice();

        /// <summary>Marks the end of the decoding of a top-level exception.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public abstract void IceEndException();

        /// <summary>Tells the decoder the end of a class was reached.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public abstract void IceEndSlice();

        /// <summary>Marks the start of the decoding of a derived exception slice.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public abstract void IceStartDerivedExceptionSlice();

        /// <summary>Marks the start of the decoding of a top-level exception.</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public abstract void IceStartException();

        /// <summary>Starts decoding the first slice of a class.</summary>
        /// <returns>The sliced-off slices, if any.</returns>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public abstract SlicedData? IceStartFirstSlice();

        /// <summary>Starts decoding a base slice of a class instance (any slice except the first slice).</summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public abstract void IceStartNextSlice();

        /// <summary>Constructs a new Ice decoder over a byte buffer.</summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        private protected IceDecoder(ReadOnlyMemory<byte> buffer, Connection? connection, IInvoker? invoker)
        {
            Connection = connection;
            Invoker = invoker;
            Pos = 0;
            _buffer = buffer;
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

         // <summary>Decodes a field.</summary>
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

        /// <summary>Checks if the decoder holds a tagged proxy for the given tag, and when it does, skips the size
        /// of this proxy.</summary>
        /// <param name="tag">The tag.</param>
        /// <returns>True when the next bytes correspond to the proxy; otherwise, false.</returns>
        internal bool DecodeTaggedProxyHeader(int tag)
        {
            if (DecodeTaggedParamHeader(tag, EncodingDefinitions.TagFormat.FSize))
            {
                SkipFixedLengthSize();
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

        /// <summary>Decodes a sequence size and makes sure there is enough space in the underlying buffer to decode the
        /// sequence. This validation is performed to make sure we do not allocate a large container based on an
        /// invalid encoded size.</summary>
        /// <param name="minElementSize">The minimum encoded size of an element of the sequence, in bytes. This value is
        /// 0 for sequence of nullable types other than mapped Slice classes and proxies.</param>
        /// <returns>The number of elements in the sequence.</returns>
        private protected int DecodeAndCheckSeqSize(int minElementSize)
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

        /// <summary>Determines if a tagged parameter or data member is available.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="expectedFormat">The expected format of the tagged parameter.</param>
        /// <returns>True if the tagged parameter is present; otherwise, false.</returns>
        private protected virtual bool DecodeTaggedParamHeader(int tag, EncodingDefinitions.TagFormat expectedFormat)
        {
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

        private protected int ReadSpan(Span<byte> span)
        {
            int length = Math.Min(span.Length, _buffer.Length - Pos);
            _buffer.Span.Slice(Pos, length).CopyTo(span);
            Pos += length;
            return length;
        }

        /// <summary>Skips over a size value encoded on a fixed number of bytes.</summary>
        private protected abstract void SkipFixedLengthSize();

        /// <summary>Skips over a size value encoded on a variable number of bytes.</summary>
        private protected abstract void SkipSize();

        private protected abstract void SkipTagged(EncodingDefinitions.TagFormat format);

        private protected void SkipTaggedParams()
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
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
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
            DecodeFunc<TKey> keyDecodeFunc,
            DecodeFunc<TValue> valueDecodeFunc)
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

            protected CollectionBase(IceDecoder decoder, int minElementSize)
            {
                Count = decoder.DecodeAndCheckSeqSize(minElementSize);
                IceDecoder = decoder;
            }
        }

        // Collection<T> holds the size of a Slice sequence and decodes the sequence elements on-demand. It does not
        // fully implement IEnumerable<T> and ICollection<T> (i.e. some methods throw NotSupportedException) because
        // it's not resettable: you can't use it to decode the same bytes multiple times.
        private sealed class Collection<T> : CollectionBase<T>
        {
            private readonly DecodeFunc<T> _decodeFunc;
            internal Collection(IceDecoder decoder, int minElementSize, DecodeFunc<T> decodeFunc)
                : base(decoder, minElementSize) => _decodeFunc = decodeFunc;

            private protected override T Decode(int pos)
            {
                Debug.Assert(pos < Count);
                return _decodeFunc(IceDecoder);
            }
        }

        // Similar to Collection<T>, except we are decoding a sequence<T?> where T is a reference type. T here must not
        // correspond to a mapped Slice class or to a proxy class.
        private sealed class NullableCollection<T> : CollectionBase<T?> where T : class
        {
            private readonly ReadOnlyMemory<byte> _bitSequenceMemory;
            readonly DecodeFunc<T> _decodeFunc;

            internal NullableCollection(IceDecoder decoder, DecodeFunc<T> decodeFunc)
                : base(decoder, 0)
            {
                _bitSequenceMemory = decoder.DecodeBitSequenceMemory(Count);
                _decodeFunc = decodeFunc;
            }

            private protected override T? Decode(int pos)
            {
                Debug.Assert(pos < Count);
                var bitSequence = new ReadOnlyBitSequence(_bitSequenceMemory.Span);
                return bitSequence[pos] ? _decodeFunc(IceDecoder) : null;
            }
        }

        // Similar to Collection<T>, except we are decoding a sequence<T?> where T is a value type.
        private sealed class NullableValueCollection<T> : CollectionBase<T?> where T : struct
        {
            private readonly ReadOnlyMemory<byte> _bitSequenceMemory;
            private readonly DecodeFunc<T> _decodeFunc;

            internal NullableValueCollection(IceDecoder decoder, DecodeFunc<T> decodeFunc)
                : base(decoder, 0)
            {
                _bitSequenceMemory = decoder.DecodeBitSequenceMemory(Count);
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
