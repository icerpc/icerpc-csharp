// Copyright (c) ZeroC, Inc.

using System.Collections;
using System.Diagnostics;

namespace IceRpc.Features.Internal;

/// <summary>A feature collection decorator that does not allow updates to the underlying feature collection.</summary>
internal class ReadOnlyFeatureCollectionDecorator : IFeatureCollection
{
    /// <inheritdoc/>
    public bool IsReadOnly => true;

    private readonly IFeatureCollection _decoratee;

    /// <inheritdoc/>
    public object? this[Type key]
    {
        get => _decoratee[key];
        set => throw new InvalidOperationException("Cannot update a read-only feature collection.");
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => _decoratee.GetEnumerator();

    /// <inheritdoc />
    public TFeature? Get<TFeature>() => _decoratee.Get<TFeature>();

    /// <inheritdoc />
    public void Set<TFeature>(TFeature? feature) => _decoratee.Set(feature);

    /// <summary>Constructs a read-only feature collection over another feature collection.</summary>
    /// <param name="decoratee">The decoratee.</param>
    internal ReadOnlyFeatureCollectionDecorator(IFeatureCollection decoratee)
    {
        Debug.Assert(!decoratee.IsReadOnly);
        _decoratee = decoratee;
    }
}
