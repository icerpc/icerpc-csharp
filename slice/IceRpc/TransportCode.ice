 // Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>

// TODO: move to Interop

module IceRpc
{
    /// TransportCode is used by the Ice 1.1 encoding to encode a transport name (such as "tcp") as a short value.
    unchecked enum TransportCode : short
    {
        /// This special code means the Ice 1.1 endpoint encapsulation contains an EndpointData, and EndpoinData starts
        /// with the transport name. TODO: change value to -1?
        Any = -2,

        /// Loc pseudo-transport. TODO: remove & use Any
        Loc = -1,

        /// Colocated transport. TODO: remove since we should never encode a coloc endpoint.
        Coloc = 0,

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
}
