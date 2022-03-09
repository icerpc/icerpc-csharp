// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice.Internal;
using System.Buffers;

namespace IceRpc.Slice
{
    /// <summary>Provides extension method for field dictionaries.</summary>
    public static class FieldsExtensions
    {
        /// <summary>Retrieves the decoded field value associated with a field key.</summary>
        /// <param name="fields">The field dictionary.</param>
        /// <param name="key">The key to lookup in the field dictionary.</param>
        /// <param name="decodeFunc">The function used to decode the field value.</param>
        /// <returns>The decoded field value, or default(T?) if the key was not found in <paramref name="fields"/>.
        /// </returns>
        public static T? DecodeValue<T>(
            this IDictionary<int, ReadOnlySequence<byte>> fields,
            int key,
            DecodeFunc<T> decodeFunc) =>
            fields.TryGetValue(key, out ReadOnlySequence<byte> value) ?
                Encoding.Slice20.DecodeBuffer(value, decodeFunc) : default(T?);
    }
}
