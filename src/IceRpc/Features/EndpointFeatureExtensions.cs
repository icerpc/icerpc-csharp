// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features;

/// <summary>Extension methods for interface <see cref="IEndpointFeature"/>.</summary>
public static class EndpointFeatureExtensions
{
    /// <summary>Tries to remove an endpoint from this endpoint feature. If the endpoint is not found, this method
    /// does nothing.</summary>
    /// <param name="feature">The endpoint feature.</param>
    /// <param name="endpoint">The endpoint to remove from the endpoint feature.</param>
    public static void RemoveEndpoint(this IEndpointFeature feature, Endpoint endpoint)
    {
        // Filter-out the endpoint
        if (feature.Endpoint == endpoint)
        {
            feature.Endpoint = null;
        }
        feature.AltEndpoints = feature.AltEndpoints.RemoveAll(e => e == endpoint);

        if (feature.Endpoint is null && feature.AltEndpoints.Count > 0)
        {
            feature.Endpoint = feature.AltEndpoints[0];
            feature.AltEndpoints = feature.AltEndpoints.RemoveAt(0);
        }
    }
}
