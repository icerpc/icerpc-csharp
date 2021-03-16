// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>

module IceRpc
{
    /// Identifies a transport protocol that IceRPC can use to send requests and receive responses. The enumerators of
    /// Transport correspond to the transports that the IceRPC runtime knows and implements in some programming
    /// languages and for some version of the Ice protocol.
    unchecked enum Transport : short
    {
        /// Loc transport, resolved by a location resolver.
        Loc = -1,

        /// Colocated transport.
        Colocated = 0,

        /// TCP transport.
        TCP = 1,

        /// SSL transport.
        SSL = 2,

        /// UDP transport.
        UDP = 3,

        /// Web Socket transport.
        WS = 4,

        /// Secure Web Socket transport.
        WSS = 5,

        /// Bluetooth transport.
        BT = 6,

        /// Secure Bluetooth transport.
        BTS = 7,

        /// Apple iAP transport.
        iAP = 8,

        /// Secure Apple iAP transport.
        iAPS = 9,
    }

    /// The "on-the-wire" representation of an endpoint for the ice2 protocol.
    [cs:readonly]
    struct EndpointData
    {
        /// The transport.
        Transport transport;

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
