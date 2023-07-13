// Copyright (c) ZeroC, Inc.

using Slice;
using System.Buffers;

namespace IceRpc.Slice.Internal;

/// <summary>Extension methods for <see cref="SliceEncoding" />.</summary>
internal static class SliceEncodingExtensions
{
    /// <summary>Decodes a buffer.</summary>
    /// <typeparam name="T">The decoded type.</typeparam>
    /// <param name="encoding">The Slice encoding.</param>
    /// <param name="buffer">The byte buffer.</param>
    /// <param name="decodeFunc">The decode function for buffer.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="InvalidDataException">Thrown when <paramref name="decodeFunc" /> finds invalid data.
    /// </exception>
    internal static T DecodeBuffer<T>(
        this SliceEncoding encoding,
        ReadOnlySequence<byte> buffer,
        DecodeFunc<T> decodeFunc)
    {
        var decoder = new SliceDecoder(buffer, encoding);
        T result = decodeFunc(ref decoder);
        decoder.CheckEndOfBuffer();
        return result;
    }
}
