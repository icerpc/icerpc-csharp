// Copyright (c) ZeroC, Inc. All rights reserved.

[cs:internal]
module IceRpc::Slice::Internal
{
    /// The "on-the-wire" representation of an endpoint when using the Slice 2.0 encoding.
    [cs:readonly]
    struct EndpointData
    {
        /// The host name or address. Its exact meaning depends on the transport. For IP-based transports, it's a DNS
        /// name or IP address. For Bluetooth RFCOMM, it's a Bluetooth Device Address.
        host: string,

        /// The port number. Its exact meaning depends on the transport.
        port: ushort,

        /// The endpoint parameters.
        params: dictionary<string, string>,
    }
}
