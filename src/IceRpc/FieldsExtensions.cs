// Copyright (c) ZeroC, Inc.

// TODO: temporary, for paramref. See #4220.
#pragma warning disable CS1734 // XML comment has a type parameter reference that is not valid.

using IceRpc.Internal;
using System.Buffers;
using ZeroC.Slice;

namespace IceRpc;

/// <summary>Provides extension method for field dictionaries.</summary>
public static class FieldsExtensions
{
    /// <summary>Extension methods for <see cref="IDictionary{TKey, TValue}" />.</summary>
    /// <param name="fields">The field dictionary.</param>
    extension<TKey>(IDictionary<TKey, ReadOnlySequence<byte>> fields)
        where TKey : struct
    {
        /// <summary>Retrieves the decoded field value associated with a field key.</summary>
        /// <typeparam name="TValue">The type of the decoded field value.</typeparam>
        /// <param name="key">The key to lookup in the field dictionary.</param>
        /// <param name="decodeFunc">The function used to decode the field value.</param>
        /// <returns>The decoded field value, or default if the key was not found in <paramref name="fields" />.
        /// </returns>
        public TValue? DecodeValue<TValue>(TKey key, DecodeFunc<TValue> decodeFunc) =>
            fields.TryGetValue(key, out ReadOnlySequence<byte> value) ?
                SliceEncoding.Slice2.DecodeBuffer(value, decodeFunc) : default;
    }

    /// <summary>Extension methods for <see cref="IDictionary{TKey, TValue}" />.</summary>
    /// <param name="fields">A fields dictionary.</param>
    extension<TKey>(IDictionary<TKey, OutgoingFieldValue> fields)
        where TKey : struct
    {
        /// <summary>Sets an entry in the outgoing fields dictionary and returns the fields dictionary. If
        /// <paramref name="fields" /> is read-only, a copy is created, modified and then returned.</summary>
        /// <typeparam name="TValue">The type of the value to encode.</typeparam>
        /// <param name="key">The key of the entry to set.</param>
        /// <param name="value">The value of the entry to set.</param>
        /// <param name="encodeAction">The encode action.</param>
        /// <param name="encoding">The encoding.</param>
        /// <returns>The fields dictionary.</returns>
        public IDictionary<TKey, OutgoingFieldValue> With<TValue>(
            TKey key,
            TValue value,
            EncodeAction<TValue> encodeAction,
            SliceEncoding encoding = SliceEncoding.Slice2)
        {
            var result = fields;
            if (result.IsReadOnly)
            {
                result = new Dictionary<TKey, OutgoingFieldValue>(result);
            }

            result[key] = new OutgoingFieldValue(bufferWriter =>
            {
                var encoder = new SliceEncoder(bufferWriter, encoding);
                encodeAction(ref encoder, value);
            });
            return result;
        }

        /// <summary>Sets an entry in the outgoing fields dictionary and returns the fields dictionary. If
        /// <paramref name="fields" /> is read-only, a copy is created, modified then returned.</summary>
        /// <param name="key">The key of the entry to set.</param>
        /// <param name="value">The value of the entry to set.</param>
        /// <returns>The fields dictionary.</returns>
        public IDictionary<TKey, OutgoingFieldValue> With(TKey key, ReadOnlySequence<byte> value)
        {
            var result = fields;
            if (result.IsReadOnly)
            {
                result = new Dictionary<TKey, OutgoingFieldValue>(result);
            }
            result[key] = new OutgoingFieldValue(value);
            return result;
        }

        /// <summary>Removes an entry in the fields dictionary and returns the fields dictionary. If
        /// <paramref name="fields" /> is read-only and contains the value, a copy is created, modified then returned.
        /// </summary>
        /// <param name="key">The key of the entry to check.</param>
        /// <returns>The fields dictionary.</returns>
        public IDictionary<TKey, OutgoingFieldValue> Without(TKey key)
        {
            var result = fields;
            if (result.IsReadOnly)
            {
                if (result.ContainsKey(key))
                {
                    result = new Dictionary<TKey, OutgoingFieldValue>(result);
                    _ = result.Remove(key);
                }
            }
            else
            {
                _ = result.Remove(key);
            }
            return result;
        }
    }
}
