// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice.Internal
{
    // TODO: update Slic and remove this class
    internal class BufferWriter
    {
        /// <summary>Represents a position in the underlying buffer list. This position consists of the index of the
        /// buffer in the list and the offset into that buffer.</summary>
        internal record struct Position
        {
            /// <summary>The zero based index of the buffer.</summary>
            internal int Buffer;

            /// <summary>The offset into the buffer.</summary>
            internal int Offset;

            /// <summary>Creates a new position from the buffer and offset values.</summary>
            /// <param name="buffer">The zero based index of the buffer in the buffer list.</param>
            /// <param name="offset">The offset into the buffer.</param>
            internal Position(int buffer, int offset)
            {
                Buffer = buffer;
                Offset = offset;
            }
        }
    }
}
