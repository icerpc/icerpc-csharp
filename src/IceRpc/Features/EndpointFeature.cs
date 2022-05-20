// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Features;

/// <summary>The default implementation of <see cref="IEndpointFeature"/>.</summary>
public sealed class EndpointFeature : IEndpointFeature
{
    /// <inheritdoc/>
    public IEnumerable<Endpoint> AltEndpoints { get; set; }

    /// <inheritdoc/>
    public Endpoint? Endpoint { get; set; }

    /// <summary>Constructs an endpoint feature that uses a proxy's endpoints.</summary>
    /// <param name="proxy">The proxy to copy the endpoints from.</param>
    public EndpointFeature(Proxy proxy)
    {
        Endpoint = proxy.Endpoint;
        AltEndpoints = proxy.AltEndpoints;
    }
}
