// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features;

/// <summary>A collection of features carried by <see cref="IncomingRequest"/>, <see cref="OutgoingRequest"/> or
/// <see cref="IConnection"/>. It's very similar but not identical to the IFeatureCollection in
/// Microsoft.AspNetCore.Http.Features.</summary>
public interface IFeatureCollection : IEnumerable<KeyValuePair<Type, object>>
{
    /// <summary>Indicates whether this feature collection is read-only or read-write.</summary>
    bool IsReadOnly { get; }

    /// <summary>Gets or sets a feature. Setting null removes the feature.</summary>
    /// <param name="key">The feature key.</param>
    /// <returns>The requested feature.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Microsoft.Design",
        "CA1043:Use Integral Or String Argument For Indexers",
        Justification = "IFeatureCollection relies on usage of type as the key")]
    object? this[Type key] { get; set; }

    /// <summary>Gets the requested feature. If the feature is not set, returns null.</summary>
    /// <typeparam name="TFeature">The feature key.</typeparam>
    /// <returns>The requested feature.</returns>
    TFeature? Get<TFeature>() => this[typeof(TFeature)] is object value ? (TFeature)value : default;

    /// <summary>Sets a new feature. Setting null removes the feature.</summary>
    /// <typeparam name="TFeature">The feature key.</typeparam>
    /// <param name="feature">The feature value.</param>
    void Set<TFeature>(TFeature? feature) => this[typeof(TFeature)] = feature;
}
