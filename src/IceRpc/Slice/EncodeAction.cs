// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>A delegate that encodes into an Ice encoder.</summary>
    /// <param name="encoder">The Ice encoder.</param>
    public delegate void EncodeAction(ref IceEncoder encoder);

    /// <summary>A delegate that encodes a value with an Ice encoder.</summary>
    /// <typeparam name="T">The type of the value to encode.</typeparam>
    /// <param name="encoder">The Ice encoder.</param>
    /// <param name="value">The value to encode with the encoder.</param>
    public delegate void EncodeAction<in T>(ref IceEncoder encoder, T value);

    /// <summary>A delegate that encodes a tuple passed as in-reference with an Ice encoder.</summary>
    /// <typeparam name="T">The type of the tuple to encode.</typeparam>
    /// <param name="encoder">The Ice encoder.</param>
    /// <param name="value">The tuple to encode with the encoder.</param>
    public delegate void TupleEncodeAction<T>(ref IceEncoder encoder, in T value) where T : struct;
}
