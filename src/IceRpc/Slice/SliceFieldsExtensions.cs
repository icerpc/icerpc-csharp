// Copyright (c) ZeroC, Inc.

using Slice;

namespace IceRpc.Slice;

/// <summary>Provides an extension method for field dictionaries.</summary>
public static class SliceFieldsExtensions
{
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
}
