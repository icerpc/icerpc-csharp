// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>A delegate that encodes a value using the Ice encoding.</summary>
    /// <typeparam name="T">The type of the value to encode.</typeparam>
    /// <param name="writer">The buffer writer.</param>
    /// <param name="value">The value to encode into the buffer.</param>
    public delegate void Encoder<in T>(BufferWriter writer, T value);

    /// <summary>A delegate that encodes a tuple passed as in-reference using the Ice encoding.</summary>
    /// <typeparam name="T">The type of the tuple to encode.</typeparam>
    /// <param name="writer">The buffer writer.</param>
    /// <param name="value">The tuple to encode into the buffer.</param>
    public delegate void TupleEncoder<T>(BufferWriter writer, in T value) where T : struct;
}
