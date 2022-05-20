// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features.Internal;

namespace IceRpc.Features;

/// <summary>Provides extension methods for <see cref="IFeatureCollection"/>.</summary>
public static class FeatureCollectionExtensions
{
    /// <summary>Creates a read-only collection decorator over this feature collection.</summary>
    /// <param name="features">This feature collection.</param>
    /// <returns>A new read-only decorator over this feature collection, or the feature collection itself if it's
    /// already read-only.</returns>
    public static IFeatureCollection AsReadOnly(this IFeatureCollection features) =>
        features.IsReadOnly ? features : new ReadOnlyFeatureCollectionDecorator(features);

    /// <summary>Gets the requested feature. If the feature is not set, tries to find it in the fallback feature
    /// collection.</summary>
    /// <typeparam name="TFeature">The feature key.</typeparam>
    /// <param name="features">The main feature collection.</param>
    /// <param name="fallback">The fallback feature collection.</param>
    /// <returns>The requested feature.</returns>
    public static TFeature? Get<TFeature>(this IFeatureCollection features, IFeatureCollection fallback) =>
        features.Get<TFeature>() ?? fallback.Get<TFeature>();

    /// <summary>Updates this feature collection (if read-write) or creates a new feature collection (if read-only)
    /// and sets its T to the provided value.</summary>
    /// <paramtype name="T">The type of the value to set in the feature collection.</paramtype>
    /// <param name="features">This feature collection.</param>
    /// <param name="value">The new value.</param>
    /// <returns>The updated feature collection.</returns>
    public static IFeatureCollection With<T>(this IFeatureCollection features, T value)
    {
        if (features.IsReadOnly)
        {
            features = new FeatureCollection(features);
        }
        features.Set(value);
        return features;
    }
}
