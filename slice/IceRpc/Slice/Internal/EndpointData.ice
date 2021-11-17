// Copyright (c) ZeroC, Inc. All rights reserved.

// TODO: use generated internal types once supported
module IceRpc::Slice::Internal
{
    /// The "on-the-wire" representation of an endpoint when using the Ice 2.0 encoding.
    [cs:readonly]
    struct EndpointData
    {
        /// The protocol.
        ProtocolCode protocol;

        /// The name of the transport, for example tcp.
        string transport;

        /// The host name or address. Its exact meaning depends on the transport. For IP-based transports, it's a DNS
        /// name or IP address. For Bluetooth RFCOMM, it's a Bluetooth Device Address.
        string host;

        /// The port number. Its exact meaning depends on the transport.
        ushort port;

        /// The endpoint parameters.
        sequence<EndpointParam> params;
    }
}
