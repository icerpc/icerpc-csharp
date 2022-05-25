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
        // Filter-out the remote endpoint
        if (feature.Endpoint == endpoint)
        {
            feature.Endpoint = null;
        }
        feature.AltEndpoints = feature.AltEndpoints.Where(e => e != endpoint).ToList();

        if (feature.Endpoint == null && feature.AltEndpoints.Any())
        {
            feature.Endpoint = feature.AltEndpoints.First();
            feature.AltEndpoints = feature.AltEndpoints.Skip(1);
        }
    }
}
