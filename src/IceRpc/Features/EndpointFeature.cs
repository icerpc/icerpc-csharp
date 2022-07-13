// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Features;

/// <summary>The default implementation of <see cref="IEndpointFeature"/>.</summary>
public sealed class EndpointFeature : IEndpointFeature
{
    /// <inheritdoc/>
    public ImmutableList<Endpoint> AltEndpoints { get; set; }

    /// <inheritdoc/>
    public Endpoint? Endpoint { get; set; }

    /// <summary>Constructs an endpoint feature that uses the endpoints of a service address.</summary>
    /// <param name="serviceAddress">The service address to copy the endpoints from.</param>
    public EndpointFeature(ServiceAddress serviceAddress)
    {
        Endpoint = serviceAddress.Endpoint;
        AltEndpoints = serviceAddress.AltEndpoints;
    }
}
