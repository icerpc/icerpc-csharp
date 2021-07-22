// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>
#include <IceRpc/TransportCode.ice>

module IceRpc
{
    /// The "on-the-wire" representation of an endpoint when using the 2.0 encoding.
    [cs:readonly]
    struct EndpointData
    {
        /// The transport.
        TransportCode transportCode;

        /// The host name or address. Its exact meaning depends on the transport. For IP-based transports, it's a DNS
        /// name or IP address. For Bluetooth RFCOMM, it's a Bluetooth Device Address.
        string host;

        /// The port number. Its exact meaning depends on the transport. For IP-based transports, it's a port number.
        /// For Bluetooth RFCOMM, it's always 0.
        ushort port;

        /// A sequence of options. With tcp, ssl and udp, options is always empty. With ws and wss, option may include
        /// a single entry with a "resource" string.
        StringSeq options;
    }

    // Sequence of EndpointData (temporary).
    sequence<EndpointData> EndpointDataSeq;
}
