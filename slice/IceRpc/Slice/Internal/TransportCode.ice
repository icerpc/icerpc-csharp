 // Copyright (c) ZeroC, Inc. All rights reserved.

[cs:internal]
module IceRpc::Slice::Internal
{
    /// TransportCode is used by the Ice 1.1 encoding to encode a transport name (such as "tcp") as a short value.
    unchecked enum TransportCode : short
    {
        /// This special code means the Ice 1.1 endpoint encapsulation contains an EndpointData.
        Any = 0,

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
