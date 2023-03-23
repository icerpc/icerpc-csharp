// Copyright (c) ZeroC, Inc.

namespace IceRpc.Features;

/// <summary>A collection of features carried by <see cref="IncomingRequest" /> or <see cref="OutgoingRequest" />. It is
/// similar but not identical to the IFeatureCollection in Microsoft.AspNetCore.Http.Features.</summary>
public interface IFeatureCollection : IEnumerable<KeyValuePair<Type, object>>
{
    /// <summary>Gets a value indicating whether this feature collection is read-only or read-write.</summary>
    /// <value><see langword="true" /> if the feature collection is read-only; <see langword="false" />
    /// otherwise.</value>
    bool IsReadOnly { get; }

    /// <summary>Gets or sets a feature. Setting null removes the feature.</summary>
    /// <param name="key">The feature key.</param>
    /// <returns>The requested feature.</returns>
    object? this[Type key] { get; set; }

    /// <summary>Gets the requested feature. If the feature is not set, returns <see langword="null" />.</summary>
    /// <typeparam name="TFeature">The feature key.</typeparam>
    /// <returns>The requested feature.</returns>
    TFeature? Get<TFeature>();

    /// <summary>Sets a new feature. Setting null removes the feature.</summary>
    /// <typeparam name="TFeature">The feature key.</typeparam>
    /// <param name="feature">The feature value.</param>
    void Set<TFeature>(TFeature? feature);
}
