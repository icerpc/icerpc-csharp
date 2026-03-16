// Copyright (c) ZeroC, Inc.

using IceRpc.Ice.Codec;
using System.Buffers;
using ZeroC.Slice.Codec;

namespace IceRpc.Internal;

/// <summary>Extension methods for <see cref="ReadOnlySequence{T}" />.</summary>
internal static class ReadOnlySequenceExtensions
{
    /// <summary>Decodes an Ice buffer.</summary>
    /// <typeparam name="T">The decoded type.</typeparam>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="decodeFunc">The decode function for buffer.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="InvalidDataException">Thrown when <paramref name="decodeFunc" /> finds invalid data.
    /// </exception>
    internal static T DecodeIceBuffer<T>(this ReadOnlySequence<byte> buffer, IceRpc.Ice.Codec.DecodeFunc<T> decodeFunc)
    {
        var decoder = new IceDecoder(buffer);
        T result = decodeFunc(ref decoder);
        decoder.CheckEndOfBuffer();
        return result;
    }

    /// <summary>Decodes a Slice buffer.</summary>
    /// <typeparam name="T">The decoded type.</typeparam>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="decodeFunc">The decode function for buffer.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="InvalidDataException">Thrown when <paramref name="decodeFunc" /> finds invalid data.
    /// </exception>
    internal static T DecodeSliceBuffer<T>(this ReadOnlySequence<byte> buffer, ZeroC.Slice.Codec.DecodeFunc<T> decodeFunc)
    {
        var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
        T result = decodeFunc(ref decoder);
        decoder.CheckEndOfBuffer();
        return result;
    }
}
