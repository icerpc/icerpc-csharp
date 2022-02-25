// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>A delegate that encodes into an Slice encoder.</summary>
    /// <param name="encoder">The Slice encoder.</param>
    public delegate void EncodeAction(ref SliceEncoder encoder);

    /// <summary>A delegate that encodes a value with an Slice encoder.</summary>
    /// <typeparam name="T">The type of the value to encode.</typeparam>
    /// <param name="encoder">The Slice encoder.</param>
    /// <param name="value">The value to encode with the encoder.</param>
    public delegate void EncodeAction<in T>(ref SliceEncoder encoder, T value);

}
