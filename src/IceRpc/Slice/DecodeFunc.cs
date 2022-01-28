// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>A delegate that decodes a value from an Slice decoder.</summary>
    /// <typeparam name="T">The type of the value to decode.</typeparam>
    /// <param name="decoder">The Slice decoder.</param>
    /// <returns>The value.</returns>
    public delegate T DecodeFunc<T>(ref SliceDecoder decoder);
}
