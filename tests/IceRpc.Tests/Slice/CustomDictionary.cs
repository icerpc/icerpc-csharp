// Copyright (c) ZeroC, Inc.

using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace IceRpc.Tests.Slice;

// A custom dictionary type to test the dictionary mapping for fields. Mapping for a field of dictionary type requires
// a type that implements IDictionary<TKey, TValue> and provides a constructor with a size parameter.

public class CustomDictionary<TKey, TValue> : IDictionary<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _source;

    public CustomDictionary() => _source = new();

    public CustomDictionary(int size) => _source = new(size);

    public CustomDictionary(IDictionary<TKey, TValue> other) => _source = new(other);

    public TValue this[TKey key]
    {
        get => ((IDictionary<TKey, TValue>)_source)[key];
        set => ((IDictionary<TKey, TValue>)_source)[key] = value;
    }

    ICollection<TKey> IDictionary<TKey, TValue>.Keys => _source.Keys;

    ICollection<TValue> IDictionary<TKey, TValue>.Values => _source.Values;

    int ICollection<KeyValuePair<TKey, TValue>>.Count => _source.Count;

    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) =>
        ((ICollection<KeyValuePair<TKey, TValue>>)_source).CopyTo(array, arrayIndex);
    public bool Remove(KeyValuePair<TKey, TValue> item) =>
        ((ICollection<KeyValuePair<TKey, TValue>>)_source).Remove(item);
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) =>
        ((IDictionary<TKey, TValue>)_source).TryGetValue(key, out value);
    public void Add(TKey key, TValue value) => _source.Add(key, value);
    public void Add(KeyValuePair<TKey, TValue> item) => _source.Add(item.Key, item.Value);
    void ICollection<KeyValuePair<TKey, TValue>>.Clear() => _source.Clear();
    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) => _source.Contains(item);
    bool IDictionary<TKey, TValue>.ContainsKey(TKey key) => _source.ContainsKey(key);
    IEnumerator<KeyValuePair<TKey, TValue>> IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() =>
        _source.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _source.GetEnumerator();
    bool IDictionary<TKey, TValue>.Remove(TKey key) => _source.Remove(key);
}
