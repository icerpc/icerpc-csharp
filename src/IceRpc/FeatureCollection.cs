// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>A collection of IceRpc features used during invocations and dispatches</summary>
    public class FeatureCollection : IEnumerable<KeyValuePair<Type, object>>
    {
        /// <summary>Returns an empty read-only feature collection.</summary>
        public static FeatureCollection Empty { get; } = new FeatureCollection { IsReadOnly = true };

        /// <summary>Indicates whether this feature collection is read-only or read-write.</summary>
        public bool IsReadOnly { get; private init; }

        private readonly FeatureCollection? _defaults;

        private Dictionary<Type, object>? _features;

        /// <summary>Gets or sets a feature. Setting null removes the feature.</summary>
        /// <param name="key">The feature key.</param>
        /// <returns>The requested feature.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Design",
            "CA1043:Use Integral Or String Argument For Indexers",
            Justification = "FeatureCollection relies on usage of type as the key")]
        public object? this[Type key]
        {
            get => _features != null && _features.TryGetValue(key, out object? value) ? value : _defaults?[key];

            set
            {
                if (IsReadOnly)
                {
                    // Currently only the shared Empty feature collection is read only.
                    throw new InvalidOperationException("cannot update read-only feature collection");
                }

                if (value == null)
                {
                    _features?.Remove(key);
                }
                else
                {
                    _features ??= new Dictionary<Type, object>();
                    _features[key] = value;
                }
            }
        }

        /// <summary>Constructs an empty read-write feature collection.</summary>
        public FeatureCollection()
        {
        }

        /// <summary>Constructs a feature collection with defaults.</summary>
        /// <param name="defaults">The feature collection that provide default values.</param>
        public FeatureCollection(FeatureCollection defaults)
        {
            if (defaults != Empty)
            {
                _defaults = defaults;
            }
            // else no need to query Empty for default values
        }

        /// <summary>Gets the requested feature. If the feature is not set, returns null.</summary>
        /// <typeparam name="TFeature">The feature key.</typeparam>
        /// <returns>The requested feature.</returns>
        public TFeature? Get<TFeature>() => (TFeature?)this[typeof(TFeature)];

        /// <summary>Sets a new feature. Setting null removes the feature.</summary>
        /// <typeparam name="TFeature">The feature key.</typeparam>
        /// <param name="feature">The feature value.</param>
        public void Set<TFeature>(TFeature? feature) => this[typeof(TFeature)] = feature;

        /// <inheritdoc />
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() =>
            _features?.GetEnumerator() as IEnumerator<KeyValuePair<Type, object>> ??
            ImmutableDictionary<Type, object>.Empty.GetEnumerator();
    }
}
