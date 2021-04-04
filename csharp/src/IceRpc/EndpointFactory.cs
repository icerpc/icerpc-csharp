// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Collections.Generic;

namespace IceRpc
{
    /// <summary>Creates an endpoint from an <see cref="EndpointData"/> struct.</summary>
    /// <param name="data">The endpoint's data.</param>
    /// <param name="protocol">The endpoint's protocol.</param>
    /// <returns>A new endpoint.</returns>
    public delegate Endpoint EndpointFactory(EndpointData data, Protocol protocol);

    /// <summary>Creates an ice1 endpoint from an <see cref="InputStream"/> stream.</summary>
    /// <param name="transport">The transport of the endpoint previously read from the stream.</param>
    /// <param name="istr">The stream to read from.</param>
    /// <returns>A new endpoint for the ice1 protocol.</returns>
    public delegate Endpoint Ice1EndpointFactory(Transport transport, InputStream istr);

    /// <summary>Creates an ice1 endpoint from a parsed ice1 endpoint string.</summary>
    /// <param name="transport">The transport of the new endpoint.</param>
    /// <param name="options">The options of the new endpoint. This delegate removes any option it understands from this
    /// dictionary.</param>
    /// <param name="endpointString">The original endpoint string, for error messages and tracing.</param>
    /// <returns>A new endpoint for the ice1 protocol.</returns>
    public delegate Endpoint Ice1EndpointParser(
        Transport transport,
        Dictionary<string, string?> options,
        string endpointString);

    /// <summary>Creates an ice2 endpoint from a parsed URI.</summary>
    /// <param name="transport">The transport of the new endpoint.</param>
    /// <param name="host">The host name or IP address.</param>
    /// <param name="port">The port number.</param>
    /// <param name="options">The options of the new endpoint. This delegate removes any option it understands from this
    /// dictionary.</param>
    /// <returns>A new endpoint for the ice2 protocol.</returns>
    public delegate Endpoint Ice2EndpointParser(
        Transport transport,
        string host,
        ushort port,
        Dictionary<string, string> options);
}
