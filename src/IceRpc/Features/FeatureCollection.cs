// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections;

namespace IceRpc.Features;

/// <summary>The default read-write implementation of <see cref="IFeatureCollection"/>.</summary>
public class FeatureCollection : IFeatureCollection
{
    /// <summary>Gets a shared empty read-only instance.</summary>
    public static IFeatureCollection Empty { get; } = new FeatureCollection().AsReadOnly();

    /// <inheritdoc/>
    public bool IsReadOnly => false;

    private readonly IFeatureCollection? _defaults;
    private readonly Dictionary<Type, object> _features = new();

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Microsoft.Design",
        "CA1043:Use Integral Or String Argument For Indexers",
        Justification = "FeatureCollection relies on usage of type as the key")]
    public object? this[Type key]
    {
        get => _features.TryGetValue(key, out object? value) ? value : _defaults?[key];

        set
        {
            if (value == null)
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

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc />
    public IEnumerator<KeyValuePair<Type, object>> GetEnumerator()
    {
        foreach (KeyValuePair<Type, object> pair in _features)
        {
            yield return pair;
        }

        if (_defaults != null)
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
}
