// Copyright (c) ZeroC, Inc. All rights reserved.

[cs:internal]
module IceRpc::Slice::Internal
{
    // These definitions help with the encoding of proxies with the Ice 1.1 encoding.

    /// The identity of a service. This internal definition must match Ice::Identity.
    [cs:readonly]
    struct Identity
    {
        /// The name of the identity. An empty name is not a valid name.
        name: string,

        /// The category of the identity. Can be empty.
        category: string,
    }

    // TODO: temporary, replace by typealias Facet = [cs::type("global::IceRpc.Slice.Internal.Facet")] sequence<string>;
    [cs:readonly]
    struct Facet
    {
        // TODO: should this identifier be capitalized?
        value: sequence<string>,  // has 0 or 1 element
    }

    /// The InvocationMode is carried by proxies that use the ice protocol, and it specifies the behavior when sending
    /// a request using such a proxy.
    /// When marshaling an ice proxy, IceRPC only uses 2 values: Twoway and Datagram.
    enum InvocationMode : byte
    {
        /// This is the default invocation mode; a request using this mode always expects a response.
        Twoway,

        /// A request using oneway mode returns control to the application code as soon as it has been accepted by the
        /// local transport. Not used by IceRPC.
        Oneway,

        /// The batch oneway invocation mode is no longer supported, it was supported with Ice versions up to 3.7.
        BatchOneway,

        /// Invocation mode used by datagram based transports.
        Datagram,

        /// The batch datagram invocation mode is no longer supported, it was supported with Ice versions up to 3.7.
        BatchDatagram,
    }

    /// With the Ice 1.1 encoding, a proxy is encoded as a kind of discriminated union with:
    /// - Identity
    /// - if Identity is not the null identity:
    ///     - ProxyData11
    ///     - a sequence of endpoints that can be empty
    ///     - an adapter ID string present only when the sequence of endpoints is empty
    [cs:readonly]
    struct ProxyData11
    {
        facet: Facet,
        invocationMode: InvocationMode,
        secure: bool,
        protocolMajor: byte,
        protocolMinor: byte,
        encodingMajor: byte,
        encodingMinor: byte,
    }
}
