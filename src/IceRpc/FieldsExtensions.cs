// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using System.Buffers;

namespace IceRpc
{
    /// <summary>Provides extension method for field dictionaries.</summary>
    public static class FieldsExtensions
    {
        /// <summary>Sets an entry in the outgoing fields dictionary and returns the fields dictionary. If
        /// <paramref name="fields"/> is read-only, a copy is created, modified then returned.</summary>
        /// <paramtype name="TKey">The type of the field keys.</paramtype>
        /// <param name="fields">A fields dictionary.</param>
        /// <param name="key">The key of the entry to set.</param>
        /// <param name="value">The value of the entry to set.</param>
        /// <returns>The fields dictionary.</returns>
        public static IDictionary<TKey, OutgoingFieldValue> With<TKey>(
            this IDictionary<TKey, OutgoingFieldValue> fields,
            TKey key,
            EncodeAction value) where TKey : struct
        {
            if (fields.IsReadOnly)
            {
                fields = new Dictionary<TKey, OutgoingFieldValue>(fields);
            }
            fields[key] = new OutgoingFieldValue(value);
            return fields;
        }

        /// <summary>Sets an entry in the outgoing fields dictionary and returns the fields dictionary. If
        /// <paramref name="fields"/> is read-only, a copy is created, modified then returned.</summary>
        /// <paramtype name="TKey">The type of the field keys.</paramtype>
        /// <param name="fields">A fields dictionary.</param>
        /// <param name="key">The key of the entry to set.</param>
        /// <param name="value">The value of the entry to set.</param>
        /// <returns>The fields dictionary.</returns>
        public static IDictionary<T, OutgoingFieldValue> With<T>(
            this IDictionary<T, OutgoingFieldValue> fields,
            T key,
            ReadOnlySequence<byte> value) where T : struct
        {
            if (fields.IsReadOnly)
            {
                fields = new Dictionary<T, OutgoingFieldValue>(fields);
            }
            fields[key] = new OutgoingFieldValue(value);
            return fields;
        }

        /// <summary>Removes an entry in the fields dictionary and returns the fields dictionary. If
        /// <paramref name="fields"/> is read-only and contains the value, a copy is created, modified then returned.
        /// </summary>
        /// <paramtype name="TKey">The type of the field keys.</paramtype>
        /// <paramtype name="TValue">The type of the field values.</paramtype>
        /// <param name="fields">A fields dictionary.</param>
        /// <param name="key">The key of the entry to check.</param>
        /// <returns>The fields dictionary.</returns>
        public static IDictionary<TKey, TValue> Without<TKey, TValue>(
            this IDictionary<TKey, TValue> fields,
            TKey key) where TKey : struct
        {
            if (fields.IsReadOnly)
            {
                if (fields.ContainsKey(key))
                {
                    fields = new Dictionary<TKey, TValue>(fields);
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
