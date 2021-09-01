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

        /// <summary>Encodes a tagged value. When the value is not null, the number of bytes needed to encode the value
        /// is not known before encoding the value.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The value to encode.</param>
        /// <param name="encodeAction">A delegate that encodes <paramref name="v"/>. It's called only when
        /// <paramref name="v"/> is not null.</param>
        public void EncodeTagged<T>(int tag, T v, EncodeAction<IceEncoder, T> encodeAction)
        {
            if (v != null)
            {
                EncodeTaggedParamHeader(tag, TagFormat.FSize);
                BufferWriter.Position pos = StartFixedLengthSize();
                encodeAction(this, v);
                EndFixedLengthSize(pos);
            }
        }

        /// <summary>Encodes a tagged value. The number of bytes needed to encode the value is known before encoding the
        /// value.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The value to encode.</param>
        /// <param name="encodeAction">A delegate that encodes <paramref name="v"/>. It's called only when
        /// <paramref name="v"/> is not null.</param>
        /// <param name="size"/>The number of bytes needed to encode the value.</param>
        /// <param name="tagFormat"/>The tag format.</param>
        public void EncodeTagged<T>(
            int tag,
            T v,
            EncodeAction<IceEncoder, T> encodeAction,
            int size,
            TagFormat tagFormat)
        {
            Debug.Assert(tagFormat != TagFormat.FSize);

            if (v != null)
            {
                EncodeTaggedParamHeader(tag, GetEncodedTagFormat(tagFormat, size));

                if (tagFormat == TagFormat.VSize)
                {
                    EncodeSize(size);
                }
                encodeAction(this, v);
            }

            static TagFormat GetEncodedTagFormat(TagFormat tagFormat, int size) =>
                tagFormat switch
                {
                    TagFormat.VInt =>
                        size switch
                        {
                            1 => TagFormat.F1,
                            2 => TagFormat.F2,
                            4 => TagFormat.F4,
                            8 => TagFormat.F8,
                            _ => throw new ArgumentException($"invalid value for size: {size}", nameof(size))
                        },

                    TagFormat.OVSize => TagFormat.VSize,

                    _ => tagFormat
                };
        }

        /// <summary>Encodes a tagged sequence of fixed-size numeric values.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="v">The sequence to encode.</param>
        public void EncodeTagged<T>(int tag, ReadOnlySpan<T> v) where T : struct
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
