// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace IceRpc
{
    /// <summary>Provides extension method for field dictionaries.</summary>
    public static class FieldsExtensions
    {
        /// <summary>Retrieves the decoded field value associated with a field key.</summary>
        /// <param name="fields">The field dictionary.</param>
        /// <param name="key">The key to lookup in the field dictionary.</param>
        /// <param name="decodeFunc">The function used to decode the field value.</param>
        /// <returns>The decoded field value, of null if the key was not found in <paramref name="fields"/>.
        /// </returns>
        public static T? Get<T>(
            this IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields,
            int key,
            Func<Ice20Decoder, T> decodeFunc) where T : class =>
            fields.TryGetValue(key, out ReadOnlyMemory<byte> value) ?
                Ice20Decoder.DecodeBuffer(value, decodeFunc) : null;

        /// <summary>Retrieves the decoded field value associated with a field key.</summary>
        /// <param name="fields">The field dictionary.</param>
        /// <param name="key">The key to lookup in the field dictionary.</param>
        /// <param name="decodeFunc">The function used to decode the field value.</param>
        /// <returns>The decoded field value, of null if the key was not found in <paramref name="fields"/>.
        /// </returns>
        public static T? GetValue<T>(
            this IReadOnlyDictionary<int, ReadOnlyMemory<byte>> fields,
            int key,
            Func<Ice20Decoder, T> decodeFunc) where T : struct =>
            fields.TryGetValue(key, out ReadOnlyMemory<byte> value) ?
                Ice20Decoder.DecodeBuffer(value, decodeFunc) : null;
    }
}
