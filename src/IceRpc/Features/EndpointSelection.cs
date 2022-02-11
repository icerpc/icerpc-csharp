// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Immutable;

namespace IceRpc.Features
{
    /// <summary>A feature used by the invocation pipeline to select the target endpoint.</summary>
    public sealed class EndpointSelection
    {
        /// <summary>The alternatives to <see cref="Endpoint"/>. It should be empty when Endpoint is null.</summary>
        public IEnumerable<Endpoint> AltEndpoints { get; set; }

        /// <summary>The main target endpoint for the invocation.</summary>
        public Endpoint? Endpoint { get; set; }

        /// <summary>Constructs en endpoint selection feature without initial endpoints.</summary>
        public EndpointSelection() => AltEndpoints = ImmutableList<Endpoint>.Empty;

        /// <summary>Constructs an endpoint selection feature that uses the proxy endpoints.</summary>
        /// <param name="proxy">The proxy to copy the endpoints from.</param>
        public EndpointSelection(Proxy proxy)
        {
            Endpoint = proxy.Endpoint;
            AltEndpoints = proxy.AltEndpoints;
        }
    }
}
