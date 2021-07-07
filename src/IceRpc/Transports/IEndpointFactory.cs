// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;

namespace IceRpc.Transports
{
    /// <summary>A factory for the endpoints of a specific transport. This is a main entry point for a transport
    /// registered with the IceRPC runtime.</summary>
    /// <seealso cref="TransportRegistry"/>
    public interface IEndpointFactory
    {
        /// <summary>The name of this transport in lower case, for example "tcp".</summary>
        string Name { get; }

        /// <summary>The transport enumerator.</summary>
        Transport Transport { get; }

        /// <summary>Creates a new endpoint.</summary>
        /// <param name="data">The endpoint data.</param>
        /// <param name="protocol">The Ice protocol of the new endpoint.</param>
        Endpoint CreateEndpoint(EndpointData data, Protocol protocol);
    }

    /// <summary>An endpoint factory for a transport that supports the ice1 protocol.</summary>
    public interface IIce1EndpointFactory : IEndpointFactory
    {
        /// <summary>Reads an endpoint from a buffer.</summary>
        /// <param name="iceDecoder">The Ice decoder.</param>
        /// <returns>The new endpoint.</returns>
        Endpoint CreateIce1Endpoint(IceDecoder iceDecoder);

        /// <summary>Creates an endpoint from a pre-parsed endpoint string.</summary>
        /// <param name="options">The options parsed from the endpoint string.</param>
        /// <param name="endpointString">The source endpoint string.</param>
        /// <returns>The new endpoint.</returns>
        Endpoint CreateIce1Endpoint(Dictionary<string, string?> options, string endpointString);
    }

    /// <summary>An endpoint factory for a transport that supports the ice2 protocol.</summary>
    public interface IIce2EndpointFactory : IEndpointFactory
    {
        /// <summary>The default port for URI endpoints that don't specify a port explicitly.</summary>
        ushort DefaultUriPort { get; }

        /// <summary>Creates an endpoint from a pre-parsed endpoint URI.</summary>
        /// <param name="host">The host parsed from the URI.</param>
        /// <param name="port">The port number parsed from the URI.</param>
        /// <param name="options">The options parsed from the URI.</param>
        /// <returns>The new endpoint.</returns>
        Endpoint CreateIce2Endpoint(string host, ushort port, Dictionary<string, string> options);
    }
}
