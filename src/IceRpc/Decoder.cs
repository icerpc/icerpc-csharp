// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A delegate that decodes a value encoded with the Ice encoding.</summary>
    /// <typeparam name="T">The type of the value to decode.</typeparam>
    /// <param name="reader">The buffer reader.</param>
    /// <returns>The decoded value.</returns>
    public delegate T Decoder<T>(BufferReader reader);
}
