// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A delegate that writes a value to an Ice encoder.</summary>
    /// <typeparam name="T">The type of the value to write.</typeparam>
    /// <param name="encoder">The Ice encoder.</param>
    /// <param name="value">The value to write to the encoder.</param>
    public delegate void IceWriter<in T>(IceEncoder encoder, T value);

    /// <summary>A delegate that writes a tuple passed as in-reference to an Ice encoder.</summary>
    /// <typeparam name="T">The type of the tuple write.</typeparam>
    /// <param name="encoder">The encoder.</param>
    /// <param name="value">The tuple to write to the encoder.</param>
    public delegate void TupleIceWriter<T>(IceEncoder encoder, in T value) where T : struct;
}
