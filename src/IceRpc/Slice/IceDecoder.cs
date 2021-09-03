// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace IceRpc.Slice
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

        // The byte buffer we are decoding.
        private protected readonly ReadOnlyMemory<byte> _buffer;

        private static readonly System.Text.UTF8Encoding _utf8 = new(false, true);

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
                string value = DecodeString(_buffer.Slice(Pos, size).Span);
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
        /// <param name="checkElement">A delegate used to check each element of the array (optional).</param>
        /// <returns>The sequence decoded by this decoder, as an array.</returns>
        public T[] DecodeArray<T>(Action<T>? checkElement = null) where T : struct
        {
            int elementSize = Unsafe.SizeOf<T>();
            var value = new T[DecodeAndCheckSeqSize(elementSize)];
            int byteCount = elementSize * value.Length;
            _buffer.Span.Slice(Pos, byteCount).CopyTo(MemoryMarshal.Cast<T, byte>(value));
            Pos += byteCount;

            if (checkElement != null)
            {
                foreach (T e in value)
                {
                    checkElement(e);
                }
            }

            return value;
        }

        /// <summary>Decodes a remote exception.</summary>
        /// <returns>The remote exception.</returns>
        public abstract RemoteException DecodeException();

        /// <summary>Decodes a nullable proxy.</summary>
        /// <returns>The decoded proxy, or null.</returns>
        public abstract Proxy? DecodeNullableProxy();

        /// <summary>Decodes a proxy.</summary>
        /// <returns>The decoded proxy</returns>
        public Proxy DecodeProxy() =>
            DecodeNullableProxy() ?? throw new InvalidDataException("decoded null for a non-nullable proxy");

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

        /// <summary>Decodes a tagged parameter or data member.</summary>
        /// <param name="tag">The tag.</param>
        /// <param name="tagFormat">The expected tag format of this tag when found in the underlying buffer.</param>
        /// <param name="decodeFunc">A decode function that decodes the value of this tag.</param>
        /// <returns>The decoded value of the tagged parameter or data member, or null if not found.</returns>
        /// <remarks>When T is a value type, it should be a nullable value type such as int?.</remarks>
        public abstract T DecodeTagged<T>(int tag, TagFormat tagFormat, DecodeFunc<IceDecoder, T> decodeFunc);

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

        internal static int DecodeInt(ReadOnlySpan<byte> from) => BitConverter.ToInt32(from);
        internal static long DecodeLong(ReadOnlySpan<byte> from) => BitConverter.ToInt64(from);
        internal static short DecodeShort(ReadOnlySpan<byte> from) => BitConverter.ToInt16(from);

        /// <summary>Decodes a string from a UTF-8 byte buffer. The size of the byte buffer corresponds to the number of
        /// UTF-8 code points in the string.</summary>
        /// <param name="from">The byte buffer.</param>
        /// <returns>The string decoded from the buffer.</returns>
        internal static string DecodeString(ReadOnlySpan<byte> from) => from.IsEmpty ? "" : _utf8.GetString(from);

        internal static ushort DecodeUShort(ReadOnlySpan<byte> from) => BitConverter.ToUInt16(from);

        // Applies to all var type: varlong, varulong etc.
        internal static int DecodeVarLongLength(byte from) => 1 << (from & 0x03);

        internal static (ulong Value, int ValueLength) DecodeVarULong(ReadOnlySpan<byte> from)
        {
            ulong value = (from[0] & 0x03) switch
            {
                0 => (uint)from[0] >> 2,
                1 => (uint)BitConverter.ToUInt16(from) >> 2,
                2 => BitConverter.ToUInt32(from) >> 2,
                _ => BitConverter.ToUInt64(from) >> 2
            };

            return (value, DecodeVarLongLength(from[0]));
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

        /// <summary>Reads size bytes from the underlying buffer.</summary>
        internal ReadOnlyMemory<byte> ReadBytes(int size)
        {
            Debug.Assert(size > 0);
            var result = _buffer.Slice(Pos, size);
            Pos += size;
            return result;
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
        internal int DecodeAndCheckSeqSize(int minElementSize)
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

        internal ReadOnlyMemory<byte> DecodeBitSequenceMemory(int bitSequenceSize)
        {
            int size = (bitSequenceSize >> 3) + ((bitSequenceSize & 0x07) != 0 ? 1 : 0);
            int startPos = Pos;
            Pos += size;
            return _buffer.Slice(startPos, size);
        }

        private protected int ReadSpan(Span<byte> span)
        {
            int length = Math.Min(span.Length, _buffer.Length - Pos);
            _buffer.Span.Slice(Pos, length).CopyTo(span);
            Pos += length;
            return length;
        }

        /// <summary>Skips tagged parameters at the end of an request or response payload.</summary>
        private protected abstract void SkipTaggedParams();
    }
}
