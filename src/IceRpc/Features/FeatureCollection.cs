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

    private static readonly KeyComparer FeatureKeyComparer = new();

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
        if (_features != null)
        {
            foreach (var pair in _features)
            {
                yield return pair;
            }
        }

        if (_defaults != null)
        {
            // Don't return features masked by the wrapper.
            foreach (var pair in _features == null ? _defaults : _defaults.Except(_features, FeatureKeyComparer))
            {
                yield return pair;
            }
        }
    }

    // A key value pair equality comparer that just checks the keys
    private sealed class KeyComparer : IEqualityComparer<KeyValuePair<Type, object>>
    {
        public bool Equals(KeyValuePair<Type, object> lhs, KeyValuePair<Type, object> rhs) => lhs.Key.Equals(rhs.Key);

        public int GetHashCode(KeyValuePair<Type, object> obj) => obj.Key.GetHashCode();
    }
}
