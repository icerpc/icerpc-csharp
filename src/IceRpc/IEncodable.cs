// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Represents a type that can be encoded using the Ice encoding.</summary>
    public interface IEncodable
    {
        /// <summary>Writes this instance to the buffer using the buffer's encoding.</summary>
        /// <param name="writer">The buffer writer.</param>
        void IceWrite(BufferWriter writer);
    }
}
