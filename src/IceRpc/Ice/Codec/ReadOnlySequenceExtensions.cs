// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace IceRpc.Ice.Codec;

/// <summary>Extension methods for <see cref="ReadOnlySequence{T}" />.</summary>
public static class ReadOnlySequenceExtensions
{
    /// <summary>Decodes an Ice buffer.</summary>
    /// <typeparam name="T">The decoded type.</typeparam>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="decodeFunc">The decode function for buffer.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="InvalidDataException">Thrown when <paramref name="decodeFunc" /> finds invalid data
    /// or when the buffer is not fully consumed and contains trailing bytes after decoding.</exception>
    public static T DecodeIceBuffer<T>(this ReadOnlySequence<byte> buffer, DecodeFunc<T> decodeFunc)
    {
        var decoder = new IceDecoder(buffer);
        T result = decodeFunc(ref decoder);
        decoder.CheckEndOfBuffer();
        return result;
    }
}
