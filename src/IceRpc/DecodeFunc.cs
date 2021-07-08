// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A delegate that decodes a value from an Ice decoder.</summary>
    /// <typeparam name="T">The type of the value to decode.</typeparam>
    /// <param name="iceDecoder">The Ice decoder.</param>
    /// <returns>The value.</returns>
    public delegate T DecodeFunc<T>(IceDecoder iceDecoder);
}
