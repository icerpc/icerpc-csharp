// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
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
        internal const long VarLongMinValue = -2_305_843_009_213_693_952; // -2^61
        internal const long VarLongMaxValue = 2_305_843_009_213_693_951; // 2^61 - 1
        internal const ulong VarULongMinValue = 0;
        internal const ulong VarULongMaxValue = 4_611_686_018_427_387_903; // 2^62 - 1

        internal BufferWriter BufferWriter { get; }

        private static readonly System.Text.UTF8Encoding _utf8 = new(false, true);

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

        // Other methods

        /// <summary>Computes the minimum number of bytes required to encode a long value using the Ice encoding
        /// variable-size encoded representation.</summary>
        /// <param name="value">The long value.</param>
        /// <returns>The minimum number of bytes required to encode <paramref name="value"/>. Can be 1, 2, 4 or 8.
        /// </returns>
        public static int GetVarLongEncodedSize(long value) => 1 << GetVarLongEncodedSizeExponent(value);

        /// <summary>Computes the minimum number of bytes required to encode a ulong value using the Ice encoding
        /// variable-size encoded representation.</summary>
        /// <param name="value">The ulong value.</param>
        /// <returns>The minimum number of bytes required to encode <paramref name="value"/>. Can be 1, 2, 4 or 8.
        /// </returns>
        public static int GetVarULongEncodedSize(ulong value) => 1 << GetVarULongEncodedSizeExponent(value);

        /// <summary>Encodes a sequence of bits and returns this sequence backed by the buffer.</summary>
        /// <param name="bitSize">The minimum number of bits in the sequence.</param>
        /// <returns>The bit sequence, with all bits set. The actual size of the sequence is a multiple of 8.</returns>
        public BitSequence EncodeBitSequence(int bitSize) => BufferWriter.WriteBitSequence(bitSize);

        /// <summary>Encodes a non-null tagged value. The number of bytes needed to encode the value is not known before
        /// encoding this value.</summary>
        /// <param name="tag">The tag. Must be either FSize or OVSize.</param>
        /// <param name="tagFormat">The tag format.</param>
        /// <param name="v">The value to encode.</param>
        /// <param name="encodeAction">The delegate that encodes the value after the tag header.</param>
        public abstract void EncodeTagged<T>(
            int tag,
            TagFormat tagFormat,
            T v,
            EncodeAction<IceEncoder, T> encodeAction) where T : notnull;

        /// <summary>Encodes a non-null tagged value. The number of bytes needed to encode the value is known before
        /// encoding the value.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="tagFormat">The tag format. Can have any value except FSize.</param>
        /// <param name="size">The number of bytes needed to encode the value.</param>
        /// <param name="v">The value to encode.</param>
        /// <param name="encodeAction">The delegate that encodes the value after the tag header.</param>
        public abstract void EncodeTagged<T>(
            int tag,
            TagFormat tagFormat,
            int size,
            T v,
            EncodeAction<IceEncoder, T> encodeAction) where T : notnull;

        /// <summary>Computes the minimum number of bytes needed to encode a variable-length size.</summary>
        /// <param name="size">The size.</param>
        /// <returns>The minimum number of bytes.</returns>
        public abstract int GetSizeLength(int size);

        /// <summary>Encodes an ice1 system exception.</summary>
        /// <param name="v">The ice1 system exception to encode.</param>
        /// <returns>The reply status that corresponds to this exception.</returns>
        internal abstract ReplyStatus EncodeIce1SystemException(RemoteException v);

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

        // Constructs a Ice encoder
        private protected IceEncoder(BufferWriter bufferWriter) => BufferWriter = bufferWriter;

        private protected abstract void EncodeFixedLengthSize(int size, Span<byte> into);

        /// <summary>Gets the minimum number of bytes needed to encode a long value with the varlong encoding as an
        /// exponent of 2.</summary>
        /// <param name="value">The value to encode.</param>
        /// <returns>N where 2^N is the number of bytes needed to encode value with IceRPC's varlong encoding.</returns>
        private static int GetVarLongEncodedSizeExponent(long value)
        {
            if (value < VarLongMinValue || value > VarLongMaxValue)
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

        /// <summary>Gets the mimimum number of bytes needed to encode a long value with the varulong encoding as an
        /// exponent of 2.</summary>
        /// <param name="value">The value to encode.</param>
        /// <returns>N where 2^N is the number of bytes needed to encode value with varulong encoding.</returns>
        private static int GetVarULongEncodedSizeExponent(ulong value)
        {
            if (value > VarULongMaxValue)
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
