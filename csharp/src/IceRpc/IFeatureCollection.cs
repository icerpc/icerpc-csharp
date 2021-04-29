// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections;
using System.Collections.Generic;

namespace IceRpc
{
    /// <summary>A collection of IceRpc features used during invocations and dispatches</summary>
    public interface IFeatureCollection : IEnumerable<KeyValuePair<Type, object>>
    {

        /// <summary>Gets or sets a feature. Setting null removes the feature.</summary>
        /// <param name="key">The feature key.</param>
        /// <returns>The requested feature.</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Microsoft.Design",
            "CA1043:Use Integral Or String Argument For Indexers",
            Justification = "FeatureCollection relies on usage of type as the key")]
        public object? this[Type key] { get; set; }

        /// <summary>Gets the requested feature. If the feature is not set, returns null.</summary>
        /// <typeparam name="TFeature">The feature key.</typeparam>
        /// <returns>The requested feature.</returns>
        public TFeature? Get<TFeature>();

        /// <summary>Sets a new feature. Setting null removes the feature.</summary>
        /// <typeparam name="TFeature">The feature key.</typeparam>
        /// <param name="feature">The feature value.</param>
        public void Set<TFeature>(TFeature feature);
    }
}
