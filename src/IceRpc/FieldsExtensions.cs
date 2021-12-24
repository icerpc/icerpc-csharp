// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Slice.Internal;

namespace IceRpc
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
        public static T? Get<T>(
            this IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields,
            int key,
            DecodeFunc<T> decodeFunc) =>
            fields.TryGetValue(key, out ReadOnlyMemory<byte> value) ?
                Ice20Encoding.DecodeBuffer(value, decodeFunc) : default(T?);
    }
}
