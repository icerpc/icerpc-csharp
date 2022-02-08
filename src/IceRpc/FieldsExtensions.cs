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
            this IDictionary<int, ReadOnlyMemory<byte>> fields,
            int key,
            DecodeFunc<T> decodeFunc) =>
            fields.TryGetValue(key, out ReadOnlyMemory<byte> value) ?
                Encoding.Slice20.DecodeBuffer(value, decodeFunc) : default(T?);

        /// <summary>Sets an entry in the fields dictionary and returns the fields dictionary. If
        /// <paramref name="fields"/> is read-only, a copy is created, modified then returned.</summary>
        /// <param name="fields">A fields dictionary or similar dictionary such as fields overrides.</param>
        /// <param name="key">The key of the entry to set.</param>
        /// <param name="value">The value of the entry to set.</param>
        /// <returns>The fields dictionary.</returns>
        public static IDictionary<int, T> With<T>(this IDictionary<int, T> fields, int key, T value)
        {
            if (fields.IsReadOnly)
            {
                fields = new Dictionary<int, T>(fields);
            }
            fields[key] = value;
            return fields;
        }

        /// <summary>Removes an entry in the fields dictionary and returns the fields dictionary. If
        /// <paramref name="fields"/> is read-only and contains the value, a copy is created, modified then returned.
        /// </summary>
        /// <param name="fields">A fields dictionary or similar dictionary such as fields overrides.</param>
        /// <param name="key">The key of the entry to check.</param>
        /// <returns>The fields dictionary.</returns>
        public static IDictionary<int, T> Without<T>(this IDictionary<int, T> fields, int key)
        {
            if (fields.IsReadOnly)
            {
                if (fields.ContainsKey(key))
                {
                    fields = new Dictionary<int, T>(fields);
                    _ = fields.Remove(key);
                }
            }
            else
            {
                _ = fields.Remove(key);
            }
            return fields;
        }
    }
}
