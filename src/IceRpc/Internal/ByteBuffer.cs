// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;
using System.Runtime.InteropServices;

namespace IceRpc.Internal
{
    /// <summary>Provides extension methods for byte buffers (such as <c>ReadOnlyMemory{byte}</c>).</summary>
    internal static class ByteBuffer
    {
        private static readonly System.Text.UTF8Encoding _utf8 = new(false, true);
        internal static ReadOnlySpan<byte> AsReadOnlySpan(this Memory<byte> buffer) => buffer.Span;

        internal static void CopyTo(this ReadOnlyMemory<ReadOnlyMemory<byte>> buffers, Memory<byte> destination)
        {
            int offset = 0;
            for (int i = 0; i < buffers.Length; ++i)
            {
                buffers.Span[i].CopyTo(destination[offset..]);
                offset += buffers.Span[i].Length;
            }
        }

        internal static int GetByteCount(this ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
        {
            ReadOnlySpan<ReadOnlyMemory<byte>> span = buffers.Span;

            int count = 0;
            for (int i = 0; i < span.Length; ++i)
            {
                count += span[i].Length;
            }
            return count;
        }

        internal static int DecodeInt(this ReadOnlySpan<byte> buffer) => BitConverter.ToInt32(buffer);
        internal static long DecodeLong(this ReadOnlySpan<byte> buffer) => BitConverter.ToInt64(buffer);
        internal static short DecodeShort(this ReadOnlySpan<byte> buffer) => BitConverter.ToInt16(buffer);
        internal static ushort DecodeUShort(this ReadOnlySpan<byte> buffer) => BitConverter.ToUInt16(buffer);

        /// <summary>Decodes a string from a UTF-8 byte buffer. The size of the byte buffer corresponds to the number of
        /// UTF-8 code points in the string.</summary>
        /// <param name="buffer">The byte buffer.</param>
        /// <returns>The string decoded from the buffer.</returns>
        internal static string DecodeString(this ReadOnlySpan<byte> buffer) =>
            buffer.IsEmpty ? "" : _utf8.GetString(buffer);

        internal static (int Size, int SizeLength) DecodeSize20(this ReadOnlySpan<byte> buffer)
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
                return ((int)size, buffer[0].DecodeSizeLength20());
            }
        }

        internal static int DecodeSizeLength20(this byte b) => b.DecodeVarLongLength();

        // Applies to all var type: varlong, varulong etc.
        internal static int DecodeVarLongLength(this byte b) => 1 << (b & 0x03);

        internal static (ulong Value, int ValueLength) DecodeVarULong(this ReadOnlySpan<byte> buffer)
        {
            ulong value = (buffer[0] & 0x03) switch
            {
                0 => (uint)buffer[0] >> 2,
                1 => (uint)BitConverter.ToUInt16(buffer) >> 2,
                2 => BitConverter.ToUInt32(buffer) >> 2,
                _ => BitConverter.ToUInt64(buffer) >> 2
            };

            return (value, buffer[0].DecodeVarLongLength());
        }

        internal static IList<ArraySegment<byte>> ToSegmentList(this ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
        {
            var segments = new ArraySegment<byte>[buffers.Length];
            for (int i = 0; i < buffers.Length; ++i)
            {
                if (MemoryMarshal.TryGetArray(buffers.Span[i], out ArraySegment<byte> segment))
                {
                    segments[i] = segment;
                }
                else
                {
                    throw new ArgumentException($"{nameof(buffers)} are not backed by arrays", nameof(buffers));
                }
            }
            return segments;
        }

        internal static ReadOnlyMemory<byte> ToSingleBuffer(this ReadOnlyMemory<ReadOnlyMemory<byte>> buffers)
        {
            if (buffers.Length == 1)
            {
                return buffers.Span[0];
            }
            else
            {
                byte[] data = new byte[buffers.GetByteCount()];
                buffers.CopyTo(data);
                return data;
            }
        }
    }
}
