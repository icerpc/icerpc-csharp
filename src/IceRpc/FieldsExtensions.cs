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
        /// <param name="fields">A fields dictionary.</param>
        /// <param name="key">The key of the entry to set.</param>
        /// <param name="value">The value of the entry to set.</param>
        /// <returns>The fields dictionary.</returns>
        public static IDictionary<int, OutgoingFieldValue> With(
            this IDictionary<int, OutgoingFieldValue> fields,
            int key,
            EncodeAction value)
        {
            if (fields.IsReadOnly)
            {
                fields = new Dictionary<int, OutgoingFieldValue>(fields);
            }
            fields[key] = new OutgoingFieldValue(value);
            return fields;
        }

        /// <summary>Sets an entry in the outgoing fields dictionary and returns the fields dictionary. If
        /// <paramref name="fields"/> is read-only, a copy is created, modified then returned.</summary>
        /// <param name="fields">A fields dictionary.</param>
        /// <param name="key">The key of the entry to set.</param>
        /// <param name="value">The value of the entry to set.</param>
        /// <returns>The fields dictionary.</returns>
        public static IDictionary<int, OutgoingFieldValue> With(
            this IDictionary<int, OutgoingFieldValue> fields,
            int key,
            ReadOnlySequence<byte> value)
        {
            if (fields.IsReadOnly)
            {
                fields = new Dictionary<int, OutgoingFieldValue>(fields);
            }
            fields[key] = new OutgoingFieldValue(value);
            return fields;
        }

        /// <summary>Removes an entry in the fields dictionary and returns the fields dictionary. If
        /// <paramref name="fields"/> is read-only and contains the value, a copy is created, modified then returned.
        /// </summary>
        /// <param name="fields">A fields dictionary.</param>
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
