// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>
#include <IceRpc/Protocol.ice>

module IceRpc
{
    /// An endpoint parameter.
    [cs:readonly]
    struct EndpointParam
    {
        /// The parameter name.
        string name;

        /// The parameter value.
        string value;
    }

    // temporary
    sequence<EndpointParam> EndpointParamSeq;

    /// The "on-the-wire" representation of an endpoint when using the 2.0 encoding.
    [cs:readonly]
    struct EndpointData
    {
        /// The Ice protocol.
        Protocol protocol;

        /// The name of the transport, for example tcp.
        string transport;

        /// The host name or address. Its exact meaning depends on the transport. For IP-based transports, it's a DNS
        /// name or IP address. For Bluetooth RFCOMM, it's a Bluetooth Device Address.
        string host;

        /// The port number. Its exact meaning depends on the transport.
        ushort port;

        /// The endpoint parameters.
        EndpointParamSeq params;
    }

    // Sequence of EndpointData (temporary).
    sequence<EndpointData> EndpointDataSeq;
}
