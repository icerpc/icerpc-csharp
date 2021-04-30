// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace IceRpc.Internal
{
    /// <summary>Provides public extensions methods to manage byte array segments.</summary>
    internal static class VectoredBufferExtensions
    {
        /// <summary>Returns the sum of the count of all the array segments in the source enumerable.</summary>
        /// <param name="src">The list of segments.</param>
        /// <returns>The byte count of the segment list.</returns>
        internal static int GetByteCount(this IEnumerable<ArraySegment<byte>> src)
        {
            int count = 0;
            foreach (ArraySegment<byte> segment in src)
            {
                count += segment.Count;
            }
            return count;
        }

        internal static ArraySegment<byte> AsArraySegment(this IList<ArraySegment<byte>> src)
        {
            if (src.Count == 1)
            {
                return src[0];
            }
            else
            {
                byte[] data = new byte[src.GetByteCount()];
                int offset = 0;
                foreach (ArraySegment<byte> segment in src)
                {
                    Debug.Assert(segment.Array != null);
                    Buffer.BlockCopy(segment.Array, segment.Offset, data, offset, segment.Count);
                    offset += segment.Count;
                }
                return data;
            }
        }

        internal static byte GetByte(this IEnumerable<ArraySegment<byte>> src, int index)
        {
            Debug.Assert(index >= 0);
            foreach (ArraySegment<byte> segment in src)
            {
                if (index < segment.Count)
                {
                    return segment[index];
                }
                else
                {
                    index -= segment.Count;
                    // and skip this segment
                }
            }
            throw new IndexOutOfRangeException("index not found in vectored buffer");
        }

        internal static List<ArraySegment<byte>> Slice(
            this IList<ArraySegment<byte>> src,
            OutputStream.Position from,
            OutputStream.Position to)
        {
            var dst = new List<ArraySegment<byte>>();
            if (from.Segment == to.Segment)
            {
                dst.Add(src[from.Segment].Slice(from.Offset, to.Offset - from.Offset));
            }
            else
            {
                ArraySegment<byte> segment = src[from.Segment].Slice(from.Offset);
                if (segment.Count > 0)
                {
                    dst.Add(segment);
                }
                for (int i = from.Segment + 1; i < to.Segment; i++)
                {
                    dst.Add(src[i]);
                }

                segment = src[to.Segment].Slice(0, to.Offset);
                if (segment.Count > 0)
                {
                    dst.Add(segment);
                }
            }
            return dst;
        }

        internal static List<ArraySegment<byte>> Slice(this IEnumerable<ArraySegment<byte>> src, int start)
        {
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException($"{nameof(start)} must greater or equal to 0", nameof(start));
            }

            var result = new List<ArraySegment<byte>>();
            foreach (ArraySegment<byte> segment in src)
            {
                if (start == 0)
                {
                    result.Add(segment);
                }
                else if (segment.Count > start)
                {
                    result.Add(segment.Slice(start));
                    start = 0;
                }
                else
                {
                    start -= segment.Count; // and we skip this segment
                }
            }

            if (start > 0)
            {
                throw new ArgumentOutOfRangeException("start exceeds the buffer's length", nameof(start));
            }
            return result;
        }
    }
}
