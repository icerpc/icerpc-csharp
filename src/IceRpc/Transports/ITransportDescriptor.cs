// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IceRpc.Transports
{
    /// <summary>Describes a transport.</summary>
    /// <seealso cref="TransportRegistry"/>
    public interface ITransportDescriptor
    {
        /// <summary>The name of this transport in lower case, for example "tcp".</summary>
        string Name { get; }

        /// <summary>The transport enumerator.</summary>
        Transport Transport { get; }

        /// <summary>Creates a new endpoint.</summary>
        /// <param name="endpointData">The endpoint data.</param>
        /// <param name="protocol">The Ice protocol of the new endpoint.</param>
        Endpoint CreateEndpoint(EndpointData endpointData, Protocol protocol);
    }

    /// <summary>A transport descriptor for a transport that supports the ice1 protocol.</summary>
    public interface IIce1TransportDescriptor : ITransportDescriptor
    {
        /// <summary>Reads an endpoint from the input stream.</summary>
        /// <param name="istr">The input stream.</param>
        /// <returns>The new endpoint.</returns>
        Endpoint CreateEndpoint(InputStream istr);

        /// <summary>Creates an endpoint from a pre-parsed endpoint string.</summary>
        /// <param name="options">The options parsed from the endpoint string.</param>
        /// <param name="endpointString">The source endpoint string.</param>
        /// <returns>The new endpoint.</returns>
        Endpoint CreateEndpoint(Dictionary<string, string?> options, string endpointString);
    }

    /// <summary>A transport descriptor for a transport that supports the ice2 protocol.</summary>
    public interface IIce2TransportDescriptor : ITransportDescriptor
    {
        /// <summary>The default port for URI endpoints that don't specify a port explicitly.</summary>
        ushort DefaultUriPort { get; }

        /// <summary>Creates an endpoint from a pre-parsed endpoint URI.</summary>
        /// <param name="host">The host parsed from the URI.</param>
        /// <param name="port">The port number parsed from the URI.</param>
        /// <param name="options">The options parsed from the URI.</param>
        /// <returns>The new endpoint.</returns>
        Endpoint CreateEndpoint(string host, ushort port, Dictionary<string, string> options);
    }
}
