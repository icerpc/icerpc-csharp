// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Diagnostics;

namespace IceRpc.Slice
{
    /// <summary>Provides an extension method for class FeatureCollection, to get and set specific features.</summary>
    public static class FeatureCollectionExtensions
    {
        /// <summary>Returns the payload size from this feature collection.</summary>
        /// <param name="features">This feature collection.</param>
        /// <returns>The value of the payload size if found; otherwise 0.</returns>
        public static int GetPayloadSize(this FeatureCollection features) =>
            features.Get<PayloadSize>()?.Value ?? 0;

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

        /// <summary>A feature that holds the size of the Slice-encoded argument(s) of a request, or the size of the
        /// Slice-encoded return value or exception of a response. The stream argument or return value (if any)
        /// is not included in this size.</summary>
        private class PayloadSize
        {
            internal int Value { get; }

            internal PayloadSize(int value)
            {
                if (value <= 0)
                {
                    throw new ArgumentException("value must be greater than 0", nameof(value));
                }
                Value = value;
            }
        }
    }
}
