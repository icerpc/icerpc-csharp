// Copyright (c) ZeroC, Inc.

using System.Collections;

namespace Slice.Tests;

public class CustomSequence<T> : IList<T>
{
    private readonly List<T> _source;

    public CustomSequence(int size) => _source = new(size);

    public CustomSequence(IEnumerable<T> elements) => _source = new(elements);

    T IList<T>.this[int index] { get => ((IList<T>)_source)[index]; set => ((IList<T>)_source)[index] = value; }

    int ICollection<T>.Count => _source.Count;

    bool ICollection<T>.IsReadOnly => false;

    void ICollection<T>.Add(T item) => _source.Add(item);
    void ICollection<T>.Clear() => _source.Clear();
    bool ICollection<T>.Contains(T item) => _source.Contains(item);
    void ICollection<T>.CopyTo(T[] array, int arrayIndex) => _source.CopyTo(array, arrayIndex);
    IEnumerator<T> IEnumerable<T>.GetEnumerator() => _source.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _source.GetEnumerator();
    int IList<T>.IndexOf(T item) => _source.IndexOf(item);
    void IList<T>.Insert(int index, T item) => _source.Insert(index, item);
    bool ICollection<T>.Remove(T item) => _source.Remove(item);
    void IList<T>.RemoveAt(int index) => _source.RemoveAt(index);
}
