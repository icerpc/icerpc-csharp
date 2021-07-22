 // Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/BuiltinSequences.ice>

// TODO: move to Interop

module IceRpc
{
    /// Identifies a transport protocol used by the ice1 application protocol.The enumerators of TransportCode
    /// correspond to the transports that the IceRPC runtime knows about.
    unchecked enum TransportCode : short
    {
        /// Loc pseudo-transport.
        Loc = -1,

        /// Colocated transport.
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
