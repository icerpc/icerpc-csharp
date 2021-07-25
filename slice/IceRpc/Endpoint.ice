// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>
#include <IceRpc/Protocol.ice>
#include <IceRpc/TransportCode.ice>

module IceRpc
{
    [cs:readonly]
    struct EndpointParameter
    {
        string name;
        string value;
    }

    sequence<EndpointParameter> EndpointParameterSeq;

    /// The "on-the-wire" representation of an endpoint when using the 2.0 encoding.
    [cs:readonly]
    struct EndpointData
    {
        /// The Ice protocol.
        Protocol protocol;

        /// The name of the transport, for example tcp.
        string transportName;

        TransportCode transportCode; // temporary

        /// The host name or address. Its exact meaning depends on the transport. For IP-based transports, it's a DNS
        /// name or IP address. For Bluetooth RFCOMM, it's a Bluetooth Device Address.
        string host;

        /// The port number. Its exact meaning depends on the transport. For IP-based transports, it's a port number.
        /// For Bluetooth RFCOMM, it's always 0.
        ushort port;

        StringSeq options;

        /// The name-value parameters.
        EndpointParameterSeq parameters;
    }

    // Sequence of EndpointData (temporary).
    sequence<EndpointData> EndpointDataSeq;
}
