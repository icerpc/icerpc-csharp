// Copyright (c) ZeroC, Inc.

using System.Collections;

namespace IceRpc.Ice.Generator.Tests;

public class CustomSequence<T> : IEnumerable<T>
{
    private readonly List<T> _list;

    public CustomSequence() => _list = [];

    IEnumerator IEnumerable.GetEnumerator() => _list.GetEnumerator();

    public IEnumerator<T> GetEnumerator() => _list.GetEnumerator();

    public int Count => _list.Count;

    public T this[int index]
    {
        get => _list[index];

        set => _list[index] = value;
    }

    public void Add(T e) => _list.Add(e);

    public override bool Equals(object? obj) =>
        obj is CustomSequence<T> other && _list.SequenceEqual(other._list);

    public override int GetHashCode() => _list.GetHashCode();

    // Helper method for the tests.
    internal static CustomSequence<T> Create(IEnumerable<T> elements) => new(elements);

    private CustomSequence(IEnumerable<T> elements) => _list = new(elements);
}
