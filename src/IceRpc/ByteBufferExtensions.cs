// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IceRpc
{
    /// <summary>Provides extension methods for byte buffers (such as <c>ReadOnlyMemory{byte}</c>) to read and write
    /// data encoded using the Ice encoding.</summary>
    public static class ByteBufferExtensions
    {
        private static readonly System.Text.UTF8Encoding _utf8 = new(false, true);

        /// <summary>Reads a value from the buffer. This value cannot contain classes or exceptions.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="encoding">The encoding of the data in the buffer.</param>
        /// <param name="reader">The <see cref="InputStreamReader{T}"/> that reads the value from the buffer using an
        /// <see cref="InputStream"/>.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <returns>The value read from the buffer.</returns>
        /// <exception name="InvalidDataException">Thrown when <c>reader</c> finds invalid data or <c>reader</c> leaves
        /// unread data in the buffer.</exception>
        public static T Read<T>(
            this ReadOnlyMemory<byte> buffer,
            Encoding encoding,
            InputStreamReader<T> reader,
            Connection? connection = null,
            IInvoker? invoker = null)
        {
            var istr = new InputStream(buffer, encoding, connection, invoker);
            T result = reader(istr);
            istr.CheckEndOfBuffer(skipTaggedParams: false);
            return result;
        }

        /// <summary>Reads a value from the buffer that uses Ice 2.0 encoding. This value cannot contain classes or
        /// exceptions.</summary>
        /// <typeparam name="T">The type of the value.</typeparam>
        /// <param name="buffer">The byte buffer.</param>
        /// <param name="reader">The <see cref="InputStreamReader{T}"/> that reads the value from the buffer using an
        /// <see cref="InputStream"/>.</param>
        /// <param name="connection">The connection.</param>
        /// <param name="invoker">The invoker.</param>
        /// <returns>The value read from the buffer.</returns>
        /// <exception name="InvalidDataException">Thrown when <c>reader</c> finds invalid data or <c>reader</c> leaves
        /// unread data in the buffer.</exception>
        public static T Read<T>(
            this ReadOnlyMemory<byte> buffer,
            InputStreamReader<T> reader,
            Connection? connection = null,
            IInvoker? invoker = null) =>
            buffer.Read(Encoding.V20, reader, connection, invoker);

        internal static ReadOnlyMemory<T> AsReadOnlyMemory<T>(this ArraySegment<T> segment) => segment;

        internal static ReadOnlySpan<T> AsReadOnlySpan<T>(this ArraySegment<T> segment) => segment;

        internal static ReadOnlySpan<T> AsReadOnlySpan<T>(this ArraySegment<T> segment, int start, int length) =>
            segment.AsSpan(start, length);

        internal static int ReadInt(this ReadOnlySpan<byte> buffer) => BitConverter.ToInt32(buffer);
        internal static long ReadLong(this ReadOnlySpan<byte> buffer) => BitConverter.ToInt64(buffer);
        internal static short ReadShort(this ReadOnlySpan<byte> buffer) => BitConverter.ToInt16(buffer);
        internal static ushort ReadUShort(this ReadOnlySpan<byte> buffer) => BitConverter.ToUInt16(buffer);

        /// <summary>Reads a string from a UTF-8 byte buffer. The size of the byte buffer corresponds to the number of
        /// UTF-8 code points in the string.</summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <returns>The string read from the buffer.</returns>
        internal static string ReadString(this ReadOnlySpan<byte> buffer) =>
            buffer.IsEmpty ? "" : _utf8.GetString(buffer);

        internal static (int Size, int SizeLength) ReadSize20(this ReadOnlySpan<byte> buffer)
        {
            ulong size = (buffer[0] & 0x03) switch
            {
                0 => (uint)buffer[0] >> 2,
                1 => (uint)BitConverter.ToUInt16(buffer) >> 2,
                2 => BitConverter.ToUInt32(buffer) >> 2,
                _ => BitConverter.ToUInt64(buffer) >> 2
            };

            checked // make sure we don't overflow
            {
                return ((int)size, buffer[0].ReadSizeLength20());
            }
        }

        internal static int ReadSizeLength20(this byte b) => b.ReadVarLongLength();

        // Applies to all var type: varlong, varulong etc.
        internal static int ReadVarLongLength(this byte b) => 1 << (b & 0x03);

        internal static (ulong Value, int ValueLength) ReadVarULong(this ReadOnlySpan<byte> buffer)
        {
            ulong value = (buffer[0] & 0x03) switch
            {
                0 => (uint)buffer[0] >> 2,
                1 => (uint)BitConverter.ToUInt16(buffer) >> 2,
                2 => BitConverter.ToUInt32(buffer) >> 2,
                _ => BitConverter.ToUInt64(buffer) >> 2
            };

            return (value, buffer[0].ReadVarLongLength());
        }

        internal static void WriteEncapsulationSize(this Span<byte> buffer, int size, Encoding encoding)
        {
            if (encoding == Encoding.V20)
            {
                buffer.WriteFixedLengthSize20(size);
            }
            else
            {
                buffer.WriteInt(size + 4);
            }
        }

        /// <summary>Writes a size into a span of bytes using a fixed number of bytes.</summary>
        /// <param name="buffer">The destination byte buffer, which must be 1, 2, 4 or 8 bytes long.</param>
        /// <param name="size">The size to write.</param>
        internal static void WriteFixedLengthSize20(this Span<byte> buffer, long size)
        {
            int sizeLength = buffer.Length;
            Debug.Assert(sizeLength == 1 || sizeLength == 2 || sizeLength == 4 || sizeLength == 8);

            (uint encodedSizeExponent, long maxSize) = sizeLength switch
            {
                1 => (0x00u, 63), // 2^6 - 1
                2 => (0x01u, 16_383), // 2^14 - 1
                4 => (0x02u, 1_073_741_823), // 2^30 - 1
                _ => (0x03u, (long)EncodingDefinitions.VarULongMaxValue)
            };

            if (size < 0 || size > maxSize)
            {
                throw new ArgumentOutOfRangeException(
                    $"size '{size}' cannot be encoded on {sizeLength} bytes",
                    nameof(size));
            }

            Span<byte> ulongBuf = stackalloc byte[8];
            ulong v = (ulong)size;
            v <<= 2;

            v |= encodedSizeExponent;
            MemoryMarshal.Write(ulongBuf, ref v);
            ulongBuf.Slice(0, sizeLength).CopyTo(buffer);
        }

        internal static void WriteInt(this Span<byte> buffer, int v) => MemoryMarshal.Write(buffer, ref v);
    }
}
