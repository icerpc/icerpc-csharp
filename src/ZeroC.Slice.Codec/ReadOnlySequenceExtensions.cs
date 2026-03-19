// Copyright (c) ZeroC, Inc.

using System.Buffers;

namespace ZeroC.Slice.Codec;

/// <summary>Extension methods for <see cref="ReadOnlySequence{T}" />.</summary>
public static class ReadOnlySequenceExtensions
{
    /// <summary>Decodes a Slice buffer.</summary>
    /// <typeparam name="T">The decoded type.</typeparam>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="decodeFunc">The decode function for buffer.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="InvalidDataException">Thrown when <paramref name="decodeFunc" /> finds invalid data
    /// or when the buffer contains trailing bytes that are not consumed by <paramref name="decodeFunc" />.
    /// </exception>
    public static T DecodeSliceBuffer<T>(this ReadOnlySequence<byte> buffer, DecodeFunc<T> decodeFunc)
    {
        var decoder = new SliceDecoder(buffer);
        T result = decodeFunc(ref decoder);
        decoder.CheckEndOfBuffer();
        return result;
    }
}
