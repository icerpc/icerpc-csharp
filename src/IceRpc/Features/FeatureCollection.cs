// Copyright (c) ZeroC, Inc.

using System.Collections;

namespace IceRpc.Features;

/// <summary>The default read-write implementation of <see cref="IFeatureCollection" />.</summary>
public class FeatureCollection : IFeatureCollection
{
    /// <summary>Gets a shared empty read-only instance.</summary>
    /// <value>An empty <see cref="FeatureCollection" />.</value>
    public static IFeatureCollection Empty { get; } = new FeatureCollection().AsReadOnly();

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    private readonly IFeatureCollection? _defaults;
    private readonly Dictionary<Type, object> _features = new();

    /// <inheritdoc/>
    public object? this[Type key]
    {
        get => _features.TryGetValue(key, out object? value) ? value : _defaults?[key];

        set
        {
            if (value is null)
            {
                _ = _features.Remove(key);
            }
            else
            {
                _features[key] = value;
            }
        }
    }

    /// <summary>Constructs an empty read-write feature collection.</summary>
    public FeatureCollection()
    {
    }

    /// <summary>Constructs an empty read-write feature collection with defaults.</summary>
    /// <param name="defaults">The feature collection that provide default values.</param>
    public FeatureCollection(IFeatureCollection defaults) =>
        // no need to query the empty read-only collection for defaults; any other feature collection (even empty and
        // read-only) can change over time
        _defaults = defaults == Empty ? null : defaults;

    /// <inheritdoc/>
    public TFeature? Get<TFeature>() => this[typeof(TFeature)] is object value ? (TFeature)value : default;

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Returns an enumerator that iterates over this feature collection.</summary>
    /// <returns>An <see cref="IEnumerator{T}"/> object that can be used to iterate through this feature collection.
    /// </returns>
    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
    {
        foreach (KeyValuePair<Type, object> pair in _features)
        {
            yield return pair;
        }

        if (_defaults is not null)
        {
            foreach (KeyValuePair<Type, object> pair in _defaults)
            {
                if (!_features.ContainsKey(pair.Key))
                {
                    yield return pair;
                }
            }
        }
    }

    /// <inheritdoc/>
    public void Set<TFeature>(TFeature? feature) => this[typeof(TFeature)] = feature;
}
