// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Buffers;
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

        /// <summary>The number of bytes encoded by this encoder into the underlying buffer writer.</summary>
        internal int EncodedByteCount { get; private set; }

        private static readonly System.Text.UTF8Encoding _utf8 = new(false, true);

        private readonly IBufferWriter<byte> _bufferWriter;

        // Encode methods for basic types

        /// <summary>Encodes a boolean.</summary>
        /// <param name="v">The boolean to encode.</param>
        public void EncodeBool(bool v) => EncodeByte(v ? (byte)1 : (byte)0);

        /// <summary>Encodes a byte.</summary>
        /// <param name="v">The byte to encode.</param>
        public void EncodeByte(byte v)
        {
            Span<byte> span = _bufferWriter.GetSpan();
            span[0] = v;
            Advance(1);
        }

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
            // TODO: revwork to avoid copy
            else if (v.Length <= 100)
            {
                Span<byte> data = stackalloc byte[_utf8.GetMaxByteCount(v.Length)];
                int encoded = _utf8.GetBytes(v, data);
                EncodeSize(encoded);
                WriteByteSpan(data[0..encoded]);
            }
            else
            {
                byte[] data = _utf8.GetBytes(v);
                EncodeSize(data.Length);
                WriteByteSpan(data.AsSpan());
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
            WriteByteSpan(data[0..(1 << encodedSizeExponent)]);
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
            WriteByteSpan(data[0..(1 << encodedSizeExponent)]);
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
                WriteByteSpan(MemoryMarshal.AsBytes(v));
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

        /// <summary>Encodes a var ulong into a span of bytes using a fixed number of bytes.</summary>
        /// <param name="value">The value to encode.</param>
        /// <param name="into">The destination byte buffer, which must be 1, 2, 4 or 8 bytes long.</param>
        public static void EncodeVarULong(ulong value, Span<byte> into)
        {
            int sizeLength = into.Length;
            Debug.Assert(sizeLength == 1 || sizeLength == 2 || sizeLength == 4 || sizeLength == 8);

            (uint encodedSizeExponent, long maxSize) = sizeLength switch
            {
                1 => (0x00u, 63), // 2^6 - 1
                2 => (0x01u, 16_383), // 2^14 - 1
                4 => (0x02u, 1_073_741_823), // 2^30 - 1
                _ => (0x03u, (long)VarULongMaxValue)
            };

            if (value > (ulong)maxSize)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    $"'{value}' cannot be encoded on {sizeLength} bytes");
            }

            Span<byte> ulongBuf = stackalloc byte[8];
            value <<= 2;

            value |= encodedSizeExponent;
            MemoryMarshal.Write(ulongBuf, ref value);
            ulongBuf[0..sizeLength].CopyTo(into);
        }

        /// <summary>Encodes a sequence of bits and returns this sequence backed by the buffer.</summary>
        /// <param name="bitSize">The minimum number of bits in the sequence.</param>
        /// <returns>The bit sequence, with all bits set. The actual size of the sequence is a multiple of 8.</returns>
        public BitSequence EncodeBitSequence(int bitSize)
        {
            Debug.Assert(bitSize > 0);
            int size = (bitSize >> 3) + ((bitSize & 0x07) != 0 ? 1 : 0);

            Span<byte> firstSpan = _bufferWriter.GetSpan();

            if (size <= firstSpan.Length)
            {
                firstSpan = firstSpan[0..size];
                firstSpan.Fill(255);
                Advance(size);
                return new BitSequence(firstSpan);
            }
            else
            {
                firstSpan.Fill(255);
                Advance(firstSpan.Length);

                int remaining = size - firstSpan.Length;
                Span<byte> secondSpan = _bufferWriter.GetSpan(remaining);
                secondSpan = secondSpan[0..remaining];
                secondSpan.Fill(255);
                Advance(remaining);

                return new BitSequence(firstSpan, secondSpan);
            }
        }

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

        internal static void EncodeInt(int v, Span<byte> into) => MemoryMarshal.Write(into, ref v);

        /// <summary>Gets a placeholder to be filled-in later.</summary>
        /// <param name="size">The size of the placeholder, typically a small number like 4.</param>
        /// <returns>A buffer of length <paramref name="size"/>.</returns>
        /// <remarks>We make the assumption to the underlying buffer writer allows rewriting memory it provided even
        /// after successive calls to GetMemory/GetSpan and Advance.</remarks>
        internal Memory<byte> GetPlaceholderMemory(int size)
        {
            Debug.Assert(size > 0);
            Memory<byte> placeHolder = _bufferWriter.GetMemory(size)[0..size];
            Advance(size);
            return placeHolder;
        }

        /// <summary>Gets a placeholder to be filled-in later.</summary>
        /// <param name="size">The size of the placeholder, typically a small number like 4.</param>
        /// <returns>A buffer of length <paramref name="size"/>.</returns>
        /// <remarks>We make the assumption to the underlying buffer writer allows rewriting memory it provided even
        /// after successive calls to GetMemory/GetSpan and Advance.</remarks>
        internal Span<byte> GetPlaceholderSpan(int size)
        {
            Debug.Assert(size > 0);
            Span<byte> placeHolder = _bufferWriter.GetSpan(size)[0..size];
            Advance(size);
            return placeHolder;
        }

        /// <summary>Copies a span of bytes to the buffer writer.</summary>
        internal void WriteByteSpan(ReadOnlySpan<byte> span)
        {
            _bufferWriter.Write(span);
            EncodedByteCount += span.Length;
        }

        // Constructs a Ice encoder
        private protected IceEncoder(IBufferWriter<byte> bufferWriter) => _bufferWriter = bufferWriter;

        private protected void Advance(int count)
        {
            _bufferWriter.Advance(count);
            EncodedByteCount += count;
        }

        internal abstract void EncodeFixedLengthSize(int size, Span<byte> into);

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
            WriteByteSpan(data);
        }
    }
}
