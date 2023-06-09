// Copyright (c) ZeroC, Inc.

using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace IceRpc.Tests.Slice;

public class CustomDictionary<TKey, TValue> : List<KeyValuePair<TKey, TValue>> where TKey : notnull
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
}
