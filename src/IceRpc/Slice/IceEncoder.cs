// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc.Slice
{
    /// <summary>Encodes data into one or more byte buffers using the Ice encoding.</summary>
    public abstract class IceEncoder
    {
        private static readonly System.Text.UTF8Encoding _utf8 = new(false, true);

        internal BufferWriter BufferWriter { get; }

        // Encode methods for basic types

        /// <summary>Encodes a boolean.</summary>
        /// <param name="v">The boolean to encode.</param>
        public void EncodeBool(bool v) => EncodeByte(v ? (byte)1 : (byte)0);

        /// <summary>Encodes a byte.</summary>
        /// <param name="v">The byte to encode.</param>
        public void EncodeByte(byte v) => BufferWriter.WriteByte(v);

        /// <summary>Encodes a double.</summary>
        /// <param name="v">The double to encode.</param>
        public void EncodeDouble(double v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a float.</summary>
        /// <param name="v">The float to encode.</param>
        public void EncodeFloat(float v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes an int.</summary>
        /// <param name="v">The int to encode.</param>
        public void EncodeInt(int v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a long.</summary>
        /// <param name="v">The long to encode.</param>
        public void EncodeLong(long v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a short.</summary>
        /// <param name="v">The short to encode.</param>
        public void EncodeShort(short v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a size on variable number of bytes.</summary>
        /// <param name="v">The size to encode.</param>
        public abstract void EncodeSize(int v);

        /// <summary>Encodes a string.</summary>
        /// <param name="v">The string to encode.</param>
        public void EncodeString(string v)
        {
            if (v.Length == 0)
            {
                EncodeSize(0);
            }
            else if (v.Length <= 100)
            {
                Span<byte> data = stackalloc byte[_utf8.GetMaxByteCount(v.Length)];
                int encoded = _utf8.GetBytes(v, data);
                EncodeSize(encoded);
                BufferWriter.WriteByteSpan(data.Slice(0, encoded));
            }
            else
            {
                byte[] data = _utf8.GetBytes(v);
                EncodeSize(data.Length);
                BufferWriter.WriteByteSpan(data.AsSpan());
            }
        }

        /// <summary>Encodes a uint.</summary>
        /// <param name="v">The uint to encode.</param>
        public void EncodeUInt(uint v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a ulong.</summary>
        /// <param name="v">The ulong to encode.</param>
        public void EncodeULong(ulong v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes a ushort.</summary>
        /// <param name="v">The ushort to encode.</param>
        public void EncodeUShort(ushort v) => EncodeFixedSizeNumeric(v);

        /// <summary>Encodes an int using IceRPC's variable-size integer encoding.</summary>
        /// <param name="v">The int to encode.</param>
        public void EncodeVarInt(int v) => EncodeVarLong(v);

        /// <summary>Encodes a long using IceRPC's variable-size integer encoding, with the minimum number of bytes
        /// required by the encoding.</summary>
        /// <param name="v">The long to encode. It must be in the range [-2^61..2^61 - 1].</param>
        public void EncodeVarLong(long v)
        {
            int encodedSizeExponent = GetVarLongEncodedSizeExponent(v);
            v <<= 2;
            v |= (uint)encodedSizeExponent;
            Span<byte> data = stackalloc byte[sizeof(long)];
            MemoryMarshal.Write(data, ref v);
            BufferWriter.WriteByteSpan(data.Slice(0, 1 << encodedSizeExponent));
        }

        /// <summary>Encodes a uint using IceRPC's variable-size integer encoding.</summary>
        /// <param name="v">The uint to encode.</param>
        public void EncodeVarUInt(uint v) => EncodeVarULong(v);

        /// <summary>Encodes a ulong using IceRPC's variable-size integer encoding, with the minimum
        /// number of bytes required by the encoding.</summary>
        /// <param name="v">The ulong to encode. It must be in the range [0..2^62 - 1].</param>
        public void EncodeVarULong(ulong v)
        {
            int encodedSizeExponent = GetVarULongEncodedSizeExponent(v);
            v <<= 2;
            v |= (uint)encodedSizeExponent;
            Span<byte> data = stackalloc byte[sizeof(ulong)];
            MemoryMarshal.Write(data, ref v);
            BufferWriter.WriteByteSpan(data.Slice(0, 1 << encodedSizeExponent));
        }

        // Encode methods for constructed types

        /// <summary>Encodes an array of fixed-size numeric values, such as int and long,.</summary>
        /// <param name="v">The array of numeric values.</param>
        public void EncodeArray<T>(T[] v) where T : struct => EncodeSequence(new ReadOnlySpan<T>(v));

        /// <summary>Encodes a remote exception.</summary>
        /// <param name="v">The remote exception to encode.</param>
        public abstract void EncodeException(RemoteException v);

        /// <summary>Encodes a nullable proxy.</summary>
        /// <param name="proxy">The proxy to encode, or null.</param>
        public abstract void EncodeNullableProxy(Proxy? proxy);

        /// <summary>Encodes a proxy.</summary>
        /// <param name="proxy">The proxy to encode.</param>
        public void EncodeProxy(Proxy proxy) => EncodeNullableProxy(proxy);

        /// <summary>Encodes a sequence of fixed-size numeric values, such as int and long,.</summary>
        /// <param name="v">The sequence of numeric values represented by a ReadOnlySpan.</param>
        // This method works because (as long as) there is no padding in the memory representation of the ReadOnlySpan.
        public void EncodeSequence<T>(ReadOnlySpan<T> v) where T : struct
        {
            EncodeSize(v.Length);
            if (!v.IsEmpty)
            {
                BufferWriter.WriteByteSpan(MemoryMarshal.AsBytes(v));
            }
        }

        /// <summary>Encodes a sequence of fixed-size numeric values, such as int and long,.</summary>
        /// <param name="v">The sequence of numeric values.</param>
        public void EncodeSequence<T>(IEnumerable<T> v) where T : struct
        {
            if (v is T[] vArray)
            {
                EncodeArray(vArray);
            }
            else if (v is ImmutableArray<T> vImmutableArray)
            {
                EncodeSequence(vImmutableArray.AsSpan());
            }
            else
            {
                this.EncodeSequence(v, (encoder, element) => encoder.EncodeFixedSizeNumeric(element));
            }
        }

        // Encode methods for tagged basic types

        /// <summary>Encodes a tagged boolean.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The boolean to encode.</param>
        public void EncodeTaggedBool(int tag, bool? v)
        {
            if (v is bool value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.F1);
                EncodeBool(value);
            }
        }

        /// <summary>Encodes a tagged byte.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The byte to encode.</param>
        public void EncodeTaggedByte(int tag, byte? v)
        {
            if (v is byte value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.F1);
                EncodeByte(value);
            }
        }

        /// <summary>Encodes a tagged double.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The double to encode.</param>
        public void EncodeTaggedDouble(int tag, double? v)
        {
            if (v is double value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.F8);
                EncodeDouble(value);
            }
        }

        /// <summary>Encodes a tagged float.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The float to encode.</param>
        public void EncodeTaggedFloat(int tag, float? v)
        {
            if (v is float value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.F4);
                EncodeFloat(value);
            }
        }

        /// <summary>Encodes a tagged int.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The int to encode.</param>
        public void EncodeTaggedInt(int tag, int? v)
        {
            if (v is int value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.F4);
                EncodeInt(value);
            }
        }

        /// <summary>Encodes a tagged long.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The long to encode.</param>
        public void EncodeTaggedLong(int tag, long? v)
        {
            if (v is long value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.F8);
                EncodeLong(value);
            }
        }

        /// <summary>Encodes a tagged size.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The size.</param>
        public void EncodeTaggedSize(int tag, int? v)
        {
            if (v is int value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.Size);
                EncodeSize(value);
            }
        }

        /// <summary>Encodes a tagged short.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The short to encode.</param>
        public void EncodeTaggedShort(int tag, short? v)
        {
            if (v is short value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.F2);
                EncodeShort(value);
            }
        }

        /// <summary>Encodes a tagged string.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The string to encode.</param>
        public void EncodeTaggedString(int tag, string? v)
        {
            if (v is string value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.VSize);
                EncodeString(value);
            }
        }

        /// <summary>Encodes a tagged uint.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The uint to encode.</param>
        public void EncodeTaggedUInt(int tag, uint? v)
        {
            if (v is uint value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.F4);
                EncodeUInt(value);
            }
        }

        /// <summary>Encodes a tagged ulong.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The ulong to encode.</param>
        public void EncodeTaggedULong(int tag, ulong? v)
        {
            if (v is ulong value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.F8);
                EncodeULong(value);
            }
        }

        /// <summary>Encodes a tagged ushort.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The ushort to encode.</param>
        public void EncodeTaggedUShort(int tag, ushort? v)
        {
            if (v is ushort value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.F2);
                EncodeUShort(value);
            }
        }

        /// <summary>Encodes a tagged int using IceRPC's variable-size integer encoding.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The int to encode.</param>
        public void EncodeTaggedVarInt(int tag, int? v) => EncodeTaggedVarLong(tag, v);

        /// <summary>Encodes a tagged long using IceRPC's variable-size integer encoding.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The long to encode.</param>
        public void EncodeTaggedVarLong(int tag, long? v)
        {
            if (v is long value)
            {
                var format = (TagFormat)GetVarLongEncodedSizeExponent(value);
                EncodeTaggedParamHeader(tag, format);
                EncodeVarLong(value);
            }
        }

        /// <summary>Encodes a tagged uint using IceRPC's variable-size integer encoding.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The uint to encode.</param>
        public void EncodeTaggedVarUInt(int tag, uint? v) => EncodeTaggedVarULong(tag, v);

        /// <summary>Encodes a tagged ulong using IceRPC's variable-size integer encoding.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The ulong to encode.</param>
        public void EncodeTaggedVarULong(int tag, ulong? v)
        {
            if (v is ulong value)
            {
                var format = (TagFormat)GetVarULongEncodedSizeExponent(value);
                EncodeTaggedParamHeader(tag, format);
                EncodeVarULong(value);
            }
        }

        // Encode methods for tagged constructed types except class

        /// <summary>Encodes a tagged dictionary with fixed-size entries.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="entrySize">The size of each entry (key + value), in bytes.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the values.</param>
        public void EncodeTaggedDictionary<TKey, TValue>(
            int tag,
            IEnumerable<KeyValuePair<TKey, TValue>>? v,
            int entrySize,
            EncodeAction<IceEncoder, TKey> keyEncodeAction,
            EncodeAction<IceEncoder, TValue> valueEncodeAction)
            where TKey : notnull
        {
            Debug.Assert(entrySize > 1);
            if (v is IEnumerable<KeyValuePair<TKey, TValue>> dict)
            {
                EncodeTaggedParamHeader(tag, TagFormat.VSize);
                int count = dict.Count();
                EncodeSize(count == 0 ? 1 : (count * entrySize) + GetSizeLength(count));
                this.EncodeDictionary(dict, keyEncodeAction, valueEncodeAction);
            }
        }

        /// <summary>Encodes a tagged dictionary with variable-size elements.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the values.</param>
        public void EncodeTaggedDictionary<TKey, TValue>(
            int tag,
            IEnumerable<KeyValuePair<TKey, TValue>>? v,
            EncodeAction<IceEncoder, TKey> keyEncodeAction,
            EncodeAction<IceEncoder, TValue> valueEncodeAction)
            where TKey : notnull
        {
            if (v is IEnumerable<KeyValuePair<TKey, TValue>> dict)
            {
                EncodeTaggedParamHeader(tag, TagFormat.FSize);
                BufferWriter.Position pos = StartFixedLengthSize();
                this.EncodeDictionary(dict, keyEncodeAction, valueEncodeAction);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged dictionary with null values encoded using a bit sequence.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The dictionary to encode.</param>
        /// <param name="keyEncodeAction">The encode action for the keys.</param>
        /// <param name="valueEncodeAction">The encode action for the non-null values.</param>
        public void EncodeTaggedDictionaryWithBitSequence<TKey, TValue>(
            int tag,
            IEnumerable<KeyValuePair<TKey, TValue>>? v,
            EncodeAction<IceEncoder, TKey> keyEncodeAction,
            EncodeAction<IceEncoder, TValue> valueEncodeAction)
            where TKey : notnull
        {
            if (v is IEnumerable<KeyValuePair<TKey, TValue>> dict)
            {
                EncodeTaggedParamHeader(tag, TagFormat.FSize);
                BufferWriter.Position pos = StartFixedLengthSize();
                this.EncodeDictionaryWithBitSequence(dict, keyEncodeAction, valueEncodeAction);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged proxy.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="proxy">The proxy to encode.</param>
        public void EncodeTaggedProxy(int tag, Proxy? proxy)
        {
            if (proxy != null)
            {
                EncodeTaggedParamHeader(tag, TagFormat.FSize);
                BufferWriter.Position pos = StartFixedLengthSize();
                EncodeProxy(proxy);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged sequence of fixed-size numeric values.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to encode.</param>
        public void EncodeTaggedSequence<T>(int tag, ReadOnlySpan<T> v) where T : struct
        {
            // A null T[]? or List<T>? is implicitly converted into a default aka null ReadOnlyMemory<T> or
            // ReadOnlySpan<T>. Furthermore, the span of a default ReadOnlyMemory<T> is a default ReadOnlySpan<T>, which
            // is distinct from the span of an empty sequence. This is why the "v != null" below works correctly.
            if (v != null)
            {
                EncodeTaggedParamHeader(tag, TagFormat.VSize);
                int elementSize = Unsafe.SizeOf<T>();
                if (elementSize > 1)
                {
                    // This size is redundant and optimized out by the encoding when elementSize is 1.
                    EncodeSize(v.IsEmpty ? 1 : (v.Length * elementSize) + GetSizeLength(v.Length));
                }
                EncodeSequence(v);
            }
        }

        /// <summary>Encodes a tagged sequence of fixed-size numeric values.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to encode.</param>
        public void EncodeTaggedSequence<T>(int tag, IEnumerable<T>? v) where T : struct
        {
            if (v is IEnumerable<T> value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.VSize);

                int elementSize = Unsafe.SizeOf<T>();
                if (elementSize > 1)
                {
                    int count = value.Count(); // potentially slow Linq Count()

                    // First write the size in bytes, so that the decoder can skip it. We optimize-out this byte size
                    // when elementSize is 1.
                    EncodeSize(count == 0 ? 1 : (count * elementSize) + GetSizeLength(count));
                }
                EncodeSequence(value);
            }
        }

        /// <summary>Encodes a tagged sequence of variable-size elements.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="encodeAction">The encode action for an element.</param>
        public void EncodeTaggedSequence<T>(int tag, IEnumerable<T>? v, EncodeAction<IceEncoder, T> encodeAction)
        {
            if (v is IEnumerable<T> value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.FSize);
                BufferWriter.Position pos = StartFixedLengthSize();
                this.EncodeSequence(value, encodeAction);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged sequence of fixed-size values.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="elementSize">The fixed size of each element of the sequence, in bytes.</param>
        /// <param name="encodeAction">The encode action for an element.</param>
        public void EncodeTaggedSequence<T>(int tag, IEnumerable<T>? v, int elementSize, EncodeAction<IceEncoder, T> encodeAction)
            where T : struct
        {
            Debug.Assert(elementSize > 0);
            if (v is IEnumerable<T> value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.VSize);

                int count = value.Count(); // potentially slow Linq Count()

                if (elementSize > 1)
                {
                    // First write the size in bytes, so that the decoder can skip it. We optimize-out this byte size
                    // when elementSize is 1.
                    EncodeSize(count == 0 ? 1 : (count * elementSize) + GetSizeLength(count));
                }
                EncodeSize(count);
                foreach (T item in value)
                {
                    encodeAction(this, item);
                }
            }
        }

        /// <summary>Encodes a tagged sequence with null values encoded using a bit sequence.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to encode.</param>
        /// <param name="encodeAction">The encode action for a non-null value.</param>
        public void EncodeTaggedSequenceWithBitSequence<T>(int tag, IEnumerable<T>? v, EncodeAction<IceEncoder, T> encodeAction)
        {
            if (v is IEnumerable<T> value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.FSize);
                BufferWriter.Position pos = StartFixedLengthSize();
                this.EncodeSequenceWithBitSequence(value, encodeAction);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged fixed-size struct.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The struct to encode.</param>
        /// <param name="fixedSize">The size of the struct, in bytes.</param>
        /// <param name="encodeAction">The encode action for a non-null element.</param>
        public void EncodeTaggedStruct<T>(int tag, T? v, int fixedSize, EncodeAction<IceEncoder, T> encodeAction) where T : struct
        {
            if (v is T value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.VSize);
                EncodeSize(fixedSize);
                encodeAction(this, value);
            }
        }

        /// <summary>Encodes a tagged variable-size struct.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The struct to encode.</param>
        /// <param name="encodeAction">The encode action for a non-null element.</param>
        public void EncodeTaggedStruct<T>(int tag, T? v, EncodeAction<IceEncoder, T> encodeAction) where T : struct
        {
            if (v is T value)
            {
                EncodeTaggedParamHeader(tag, TagFormat.FSize);
                BufferWriter.Position pos = StartFixedLengthSize();
                encodeAction(this, value);
                EndFixedLengthSize(pos);
            }
        }

        // Other methods

        /// <summary>Encodes a sequence of bits and returns this sequence backed by the buffer.</summary>
        /// <param name="bitSize">The minimum number of bits in the sequence.</param>
        /// <returns>The bit sequence, with all bits set. The actual size of the sequence is a multiple of 8.</returns>
        public BitSequence EncodeBitSequence(int bitSize) => BufferWriter.WriteBitSequence(bitSize);

        /// <summary>Computes the minimum number of bytes needed to encode a variable-length size.</summary>
        /// <param name="size">The size.</param>
        /// <returns>The minimum number of bytes.</returns>
        public abstract int GetSizeLength(int size);

        internal static void EncodeInt(int v, Span<byte> into) => MemoryMarshal.Write(into, ref v);

        /// <summary>Encodes a size on 4 bytes at the specified position.</summary>
        internal void EncodeFixedLengthSize(int size, BufferWriter.Position pos)
        {
            Debug.Assert(pos.Offset >= 0);
            Span<byte> data = stackalloc byte[4];
            EncodeFixedLengthSize(size, data);
            BufferWriter.RewriteByteSpan(data, pos);
        }

        /// <summary>Computes the amount of data encoded from the start position to the current position and writes that
        /// size at the start position (as a 4-bytes size). The size does not include its own encoded length.</summary>
        /// <param name="start">The start position.</param>
        /// <returns>The size of the encoded data.</returns>
        internal int EndFixedLengthSize(BufferWriter.Position start)
        {
            int size = BufferWriter.Distance(start) - 4;
            EncodeFixedLengthSize(size, start);
            return size;
        }

        /// <summary>Returns the current position and writes placeholder for 4 bytes. The position must be used to
        /// rewrite the size later on 4 bytes.</summary>
        /// <returns>The position before writing the size.</returns>
        internal BufferWriter.Position StartFixedLengthSize()
        {
            BufferWriter.Position pos = BufferWriter.Tail;
            BufferWriter.WriteByteSpan(stackalloc byte[4]); // placeholder for future size
            return pos;
        }

        /// <summary>Gets the mimimum number of bytes needed to encode a long value with the varulong encoding as an
        /// exponent of 2.</summary>
        /// <param name="value">The value to encode.</param>
        /// <returns>N where 2^N is the number of bytes needed to encode value with varulong encoding.</returns>
        private protected static int GetVarULongEncodedSizeExponent(ulong value)
        {
            if (value > EncodingDefinitions.VarULongMaxValue)
            {
                throw new ArgumentOutOfRangeException($"varulong value '{value}' is out of range", nameof(value));
            }

            return (value << 2) switch
            {
                ulong b when b <= byte.MaxValue => 0,
                ulong s when s <= ushort.MaxValue => 1,
                ulong i when i <= uint.MaxValue => 2,
                _ => 3
            };
        }

        // Constructs a Ice encoder
        private protected IceEncoder(BufferWriter bufferWriter) => BufferWriter = bufferWriter;

        private protected abstract void EncodeFixedLengthSize(int size, Span<byte> into);

        /// <summary>Encodes the header for a tagged parameter or data member.</summary>
        /// <param name="tag">The numeric tag associated with the parameter or data member.</param>
        /// <param name="format">The tag format.</param>
        private protected abstract void EncodeTaggedParamHeader(int tag, TagFormat format);

        /// <summary>Gets the minimum number of bytes needed to encode a long value with the varlong encoding as an
        /// exponent of 2.</summary>
        /// <param name="value">The value to encode.</param>
        /// <returns>N where 2^N is the number of bytes needed to encode value with IceRPC's varlong encoding.</returns>
        private static int GetVarLongEncodedSizeExponent(long value)
        {
            if (value < EncodingDefinitions.VarLongMinValue || value > EncodingDefinitions.VarLongMaxValue)
            {
                throw new ArgumentOutOfRangeException($"varlong value '{value}' is out of range", nameof(value));
            }

            return (value << 2) switch
            {
                long b when b >= sbyte.MinValue && b <= sbyte.MaxValue => 0,
                long s when s >= short.MinValue && s <= short.MaxValue => 1,
                long i when i >= int.MinValue && i <= int.MaxValue => 2,
                _ => 3
            };
        }

        /// <summary>Encodes a fixed-size numeric value.</summary>
        /// <param name="v">The numeric value to encode.</param>
        private void EncodeFixedSizeNumeric<T>(T v) where T : struct
        {
            int elementSize = Unsafe.SizeOf<T>();
            Span<byte> data = stackalloc byte[elementSize];
            MemoryMarshal.Write(data, ref v);
            BufferWriter.WriteByteSpan(data);
        }
    }
}
