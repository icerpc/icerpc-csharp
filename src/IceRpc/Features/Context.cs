// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Features
{
    /// <summary>A feature that represents an ice1 request context or an ice2 Context request header field.</summary>
    public sealed class Context
    {
        /// <summary>The value of this context feature.</summary>
        public IDictionary<string, string> Value { get; set; } = ImmutableSortedDictionary<string, string>.Empty;
    }

    /// <summary>Provides extension methods for FeatureCollection to read and write the <see cref="Context"/> feature.
    /// </summary>
    public static class ContextExtensions
    {
        /// <summary>Returns the value of <see cref="Context"/> in this feature collection.</summary>
        /// <param name="features">This feature collection.</param>
        /// <returns>The value of Context if found; otherwise, an empty dictionary.</returns>
        public static IDictionary<string, string> GetContext(this FeatureCollection features) =>
            features.Get<Context>()?.Value ?? ImmutableSortedDictionary<string, string>.Empty;

        /// <summary>Updates this feature collection (if read-write) or creates a new feature collection (if read-only)
        /// and sets its <see cref="Context"/> feature to the provided value.</summary>
        /// <param name="features">This feature collection.</param>
        /// <param name="value">The new context value.</param>
        /// <returns>The updated feature collection.</returns>
        public static FeatureCollection WithContext(this FeatureCollection features, IDictionary<string, string> value)
        {
            if (features.IsReadOnly)
            {
                features = new FeatureCollection(features);
            }
            features.Set(new Context { Value = value });
            return features;
        }
    }
}
