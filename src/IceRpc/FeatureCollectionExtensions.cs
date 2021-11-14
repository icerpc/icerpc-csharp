// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Features.Internal;
using System.Collections.Immutable;
using System.Diagnostics;

namespace IceRpc
{
    /// <summary>Provides an extension method for class FeatureCollection, to get and set specific features.</summary>
    public static class FeatureCollectionExtensions
    {
        /// <summary>Sets the <see cref="CompressPayload"/> feature with the value <see cref="CompressPayload.Yes"/> on
        /// this feature collection.</summary>
        /// <param name="features">The feature collection to update.</param>
        /// <returns>The updated feature collection.</returns>
        public static FeatureCollection CompressPayload(this FeatureCollection features) =>
            features[typeof(CompressPayload)] != Features.CompressPayload.Yes ?
                features.With(Features.CompressPayload.Yes) : features;

        /// <summary>Returns the value of <see cref="Context"/> in this feature collection.</summary>
        /// <param name="features">This feature collection.</param>
        /// <returns>The value of Context if found; otherwise, an empty dictionary.</returns>
        public static IDictionary<string, string> GetContext(this FeatureCollection features) =>
            features.Get<Context>()?.Value ?? ImmutableSortedDictionary<string, string>.Empty;

        /// <summary>Returns the payload size from this feature collection.</summary>
        /// <param name="features">This feature collection.</param>
        /// <returns>The value of the payload size if found; otherwise 0.</returns>
        public static int GetPayloadSize(this FeatureCollection features) =>
            features.Get<PayloadSize>()?.Value ?? 0;

        /// <summary>Returns the request ID value from this feature collection.</summary>
        /// <param name="features">This feature collection.</param>
        /// <returns>The value of the request ID if found, null otherwise.</returns>
        public static int? GetRequestId(this FeatureCollection features) => features.Get<Ice1Request>()?.Id;

        /// <summary>Updates this feature collection (if read-write) or creates a new feature collection (if read-only)
        /// and sets its T to the provided value.</summary>
        /// <paramtype name="T">The type of the value to set in the feature collection.</paramtype>
        /// <param name="features">This feature collection.</param>
        /// <param name="value">The new value.</param>
        /// <returns>The updated feature collection.</returns>
        public static FeatureCollection With<T>(this FeatureCollection features, T value)
        {
            if (features.IsReadOnly)
            {
                features = new FeatureCollection(features);
            }
            features.Set(value);
            return features;
        }

        /// <summary>Updates this feature collection (if read-write) or creates a new feature collection (if read-only)
        /// and sets its <see cref="Context"/> feature to the provided value.</summary>
        /// <param name="features">This feature collection.</param>
        /// <param name="value">The new context value.</param>
        /// <returns>The updated feature collection.</returns>
        public static FeatureCollection WithContext(
            this FeatureCollection features,
            IDictionary<string, string> value) => features.With(new Context(value));

        /// <summary>Updates this feature collection (if read-write) or creates a new feature collection (if read-only)
        /// and sets the payload size.</summary>
        /// <param name="features">This feature collection.</param>
        /// <param name="value">The new value for the payload size.</param>
        /// <returns>The updated feature collection.</returns>
        public static FeatureCollection WithPayloadSize(this FeatureCollection features, int value)
        {
            if (value == 0)
            {
                if (features.GetPayloadSize() != 0)
                {
                    // Remove entry
                    Debug.Assert(!features.IsReadOnly);
                    features[typeof(PayloadSize)] = null;
                }
                return features;
            }
            else
            {
                return features.With(new PayloadSize(value));
            }
        }
    }
}
