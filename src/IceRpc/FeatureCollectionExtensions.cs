// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using System.Collections.Immutable;

namespace IceRpc
{
    /// <summary>Provides an extension method for class FeatureCollection, to get and set specific features.</summary>
    public static class FeatureCollectionExtensions
    {
        /// <summary>Sets the <see cref="CompressPayload"/> feature with the value <see cref="CompressPayload.Yes"/> on
        /// this feature collection.</summary>
        /// <param name="features">The feature collection to update.</param>
        /// <returns>The updated feature collection.</returns>
        public static FeatureCollection CompressPayload(this FeatureCollection features)
        {
            if (features[typeof(CompressPayload)] != Features.CompressPayload.Yes)
            {
                if (features.IsReadOnly)
                {
                    features = new FeatureCollection(features);
                }
                features.Set(Features.CompressPayload.Yes);
            }
            return features;
        }

        /// <summary>Returns the value of <see cref="Context"/> in this feature collection.</summary>
        /// <param name="features">This feature collection.</param>
        /// <returns>The value of Context if found; otherwise, an empty dictionary.</returns>
        public static IDictionary<string, string> GetContext(this FeatureCollection features) =>
            features.Get<Context>()?.Value ?? ImmutableSortedDictionary<string, string>.Empty;

        /// <summary>Returns the value of <see cref="RetryPolicyFeature"/> in this feature collection.</summary>
        /// <param name="features">This feature collection.</param>
        /// <returns>The value property of the RetryPolicyFeature if found; otherwise,
        /// <see cref="RetryPolicy.NoRetry"/>.</returns>
        public static RetryPolicy GetRetryPolicy(this FeatureCollection features) =>
            features.Get<RetryPolicyFeature>()?.Value ?? RetryPolicy.NoRetry;

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

        /// <summary>Updates this feature collection (if read-write) or creates a new feature collection (if read-only)
        /// and sets its <see cref="RetryPolicyFeature"/> to the provided value.</summary>
        /// <param name="features">This feature collection.</param>
        /// <param name="value">The new retry policy value.</param>
        /// <returns>The updated feature collection.</returns>
        public static FeatureCollection WithRetryPolicy(this FeatureCollection features, RetryPolicy value)
        {
            if (features.IsReadOnly)
            {
                features = new FeatureCollection(features);
            }
            features.Set(RetryPolicyFeature.FromRetryPolicy(value));
            return features;
        }
    }
}
