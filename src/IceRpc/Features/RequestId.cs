// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features
{
    /// <summary>A feature that specifies the request ID of an Ice1 request or response.</summary>
    internal sealed class RequestId
    {
        internal int Value { get; init; }
    }

    /// <summary>Provides an extension method for FeatureCollection to set the <see cref="CompressPayload"/> feature.
    /// </summary>
    public static class RequestIdExtensions
    {
        /// <summary>Returns the request ID value from this feature collection.</summary>
        /// <param name="features">This feature collection.</param>
        /// <returns>The value of the request ID if found, null otherwise.</returns>
        public static int? GetRequestId(this FeatureCollection features) => features.Get<RequestId>()?.Value;

        /// <summary>Sets the request ID on this feature collection.</summary>
        /// <param name="features">The feature collection to update.</param>
        /// <param name="requestId">The request ID.</param>
        /// <returns>The updated feature collection.</returns>
        internal static FeatureCollection WithRequestId(this FeatureCollection features, int requestId)
        {
            if (features.IsReadOnly)
            {
                features = new FeatureCollection(features);
            }
            features.Set(new RequestId { Value = requestId });
            return features;
        }
    }
}
