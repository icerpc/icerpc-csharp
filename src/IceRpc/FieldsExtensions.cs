// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using System.Buffers;
using ZeroC.Slice;

namespace IceRpc;

/// <summary>Provides extension method for field dictionaries.</summary>
public static class FieldsExtensions
{
    /// <summary>Retrieves the decoded field value associated with a field key.</summary>
    /// <typeparam name="TKey">The type of the field keys.</typeparam>
    /// <typeparam name="TValue">The type of the decoded field value.</typeparam>
    /// <param name="fields">The field dictionary.</param>
    /// <param name="key">The key to lookup in the field dictionary.</param>
    /// <param name="decodeFunc">The function used to decode the field value.</param>
    /// <returns>The decoded field value, or default if the key was not found in <paramref name="fields" />.
    /// </returns>
    public static TValue? DecodeValue<TKey, TValue>(
        this IDictionary<TKey, ReadOnlySequence<byte>> fields,
        TKey key,
        DecodeFunc<TValue> decodeFunc) where TKey : struct =>
        fields.TryGetValue(key, out ReadOnlySequence<byte> value) ?
            SliceEncoding.Slice2.DecodeBuffer(value, decodeFunc) : default;

    /// <summary>Sets an entry in the outgoing fields dictionary and returns the fields dictionary. If
    /// <paramref name="fields" /> is read-only, a copy is created, modified and then returned.</summary>
    /// <typeparam name="TKey">The type of the field key.</typeparam>
    /// <typeparam name="TValue">The type of the value to encode.</typeparam>
    /// <param name="fields">A fields dictionary.</param>
    /// <param name="key">The key of the entry to set.</param>
    /// <param name="value">The value of the entry to set.</param>
    /// <param name="encodeAction">The encode action.</param>
    /// <param name="encoding">The encoding.</param>
    /// <returns>The fields dictionary.</returns>
    public static IDictionary<TKey, OutgoingFieldValue> With<TKey, TValue>(
        this IDictionary<TKey, OutgoingFieldValue> fields,
        TKey key,
        TValue value,
        EncodeAction<TValue> encodeAction,
        SliceEncoding encoding = SliceEncoding.Slice2)
        where TKey : struct
    {
        if (fields.IsReadOnly)
        {
            fields = new Dictionary<TKey, OutgoingFieldValue>(fields);
        }

        fields[key] = new OutgoingFieldValue(bufferWriter =>
        {
            var encoder = new SliceEncoder(bufferWriter, encoding);
            encodeAction(ref encoder, value);
        });
        return fields;
    }

    /// <summary>Sets an entry in the outgoing fields dictionary and returns the fields dictionary. If
    /// <paramref name="fields" /> is read-only, a copy is created, modified then returned.</summary>
    /// <typeparam name="TKey">The type of the field key.</typeparam>
    /// <param name="fields">A fields dictionary.</param>
    /// <param name="key">The key of the entry to set.</param>
    /// <param name="value">The value of the entry to set.</param>
    /// <returns>The fields dictionary.</returns>
    public static IDictionary<TKey, OutgoingFieldValue> With<TKey>(
        this IDictionary<TKey, OutgoingFieldValue> fields,
        TKey key,
        ReadOnlySequence<byte> value) where TKey : struct
    {
        if (fields.IsReadOnly)
        {
            fields = new Dictionary<TKey, OutgoingFieldValue>(fields);
        }
        fields[key] = new OutgoingFieldValue(value);
        return fields;
    }

    /// <summary>Removes an entry in the fields dictionary and returns the fields dictionary. If
    /// <paramref name="fields" /> is read-only and contains the value, a copy is created, modified then returned.
    /// </summary>
    /// <typeparam name="TKey">The type of the field key.</typeparam>
    /// <param name="fields">A fields dictionary.</param>
    /// <param name="key">The key of the entry to check.</param>
    /// <returns>The fields dictionary.</returns>
    public static IDictionary<TKey, OutgoingFieldValue> Without<TKey>(
        this IDictionary<TKey, OutgoingFieldValue> fields,
        TKey key) where TKey : struct
    {
        if (fields.IsReadOnly)
        {
            if (fields.ContainsKey(key))
            {
                fields = new Dictionary<TKey, OutgoingFieldValue>(fields);
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
