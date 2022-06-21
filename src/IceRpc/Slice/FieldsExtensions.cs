// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Buffers;

namespace IceRpc.Slice;

/// <summary>Provides extension method for field dictionaries.</summary>
public static class FieldsExtensions
{
    /// <summary>Retrieves the decoded field value associated with a field key.</summary>
    /// <typeparam name="TKey">The type of the field keys.</typeparam>
    /// <typeparam name="TValue">The type of the decoded field value.</typeparam>
    /// <param name="fields">The field dictionary.</param>
    /// <param name="key">The key to lookup in the field dictionary.</param>
    /// <param name="decodeFunc">The function used to decode the field value.</param>
    /// <returns>The decoded field value, or default if the key was not found in <paramref name="fields"/>.
    /// </returns>
    public static TValue? DecodeValue<TKey, TValue>(
        this IDictionary<TKey, ReadOnlySequence<byte>> fields,
        TKey key,
        DecodeFunc<TValue> decodeFunc) where TKey : struct =>
        fields.TryGetValue(key, out ReadOnlySequence<byte> value) ?
            SliceEncoding.Slice2.DecodeBuffer(value, decodeFunc) : default;
}
