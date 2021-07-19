// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>Represents a type that can be encoded using the Ice encoding.</summary>
    public interface IEncodable
    {
        /// <summary>Encodes this instance.</summary>
        /// <param name="encoder">The Ice encoder.</param>
        void Encode(IceEncoder encoder);
    }
}
