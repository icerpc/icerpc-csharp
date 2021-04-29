// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;

namespace IceRpc
{
    /// <summary>The default implementation of <see cref="IFeatureCollection"/>.</summary>
    public class FeatureCollection : IFeatureCollection
    {
        private readonly Dictionary<Type, object> _features = new();

        /// <inheritdoc />
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Design",
            "CA1043:Use Integral Or String Argument For Indexers",
            Justification = "FeatureCollection relies on usage of type as the key")]
        public object? this[Type key]
        {
            get => _features.TryGetValue(key, out object? value) ? value : null;

            set
            {
                if (value == null)
                {
                    _features.Remove(key);
                }
                else
                {
                    _features[key] = value;
                }
            }
        }

        /// <inheritdoc />
        public TFeature? Get<TFeature>() => (TFeature?)this[typeof(TFeature)];

        /// <inheritdoc />
        public void Set<TFeature>(TFeature feature) => this[typeof(TFeature)] = feature;

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <inheritdoc />
        public IEnumerator<KeyValuePair<Type, object>> GetEnumerator() => _features.GetEnumerator();
    }
}
