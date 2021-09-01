// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Slice
{
    /// <summary>A delegate that encodes a value with an Ice encoder.</summary>
    /// <typeparam name="TEncoder">The type of the Ice encoder.</typeparam>
    /// <typeparam name="T">The type of the value to encode.</typeparam>
    /// <param name="encoder">The Ice encoder.</param>
    /// <param name="value">The value to encode with the encoder.</param>
    public delegate void EncodeAction<TEncoder, in T>(TEncoder encoder, T value) where TEncoder : IceEncoder;

    /// <summary>A delegate that encodes a tuple passed as in-reference with an Ice encoder.</summary>
    /// <typeparam name="TEncoder">The type of the Ice encoder.</typeparam>
    /// <typeparam name="T">The type of the tuple to encode.</typeparam>
    /// <param name="encoder">The Ice encoder.</param>
    /// <param name="value">The tuple to encode with the encoder.</param>
    public delegate void TupleEncodeAction<TEncoder, T>(TEncoder encoder, in T value)
        where TEncoder : IceEncoder
        where T : struct;
}
