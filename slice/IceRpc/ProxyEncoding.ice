// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/Interop/Identity.ice>
#include <IceRpc/BuiltinSequences.ice>
#include <IceRpc/Endpoint.ice>
#include <IceRpc/Protocol.ice>

module IceRpc
{
    // These definitions help with the encoding of proxies.

    /// The InvocationMode is carried by proxies that use the ice1 protocol, and it specifies the behavior when sending
    /// a request using such a proxy.
    /// When marshaling an ice1 proxy, IceRPC only uses 2 values: Twoway and Datagram.
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

    /// With the 1.1 encoding, a proxy is encoded as a kind of discriminated union with:
    /// - Identity
    /// - if Identity is not the null identity:
    ///     - ProxyData11
    ///     - a sequence of endpoints that can be empty
    ///     - an adapter ID string present only when the sequence of endpoints is empty
    [cs:readonly]
    struct ProxyData11
    {
        StringSeq optionalFacet;       // has 0 or 1 element
        InvocationMode invocationMode;
        bool secure = false;           // ignored
        Protocol protocol;
        byte protocolMinor = 0;        // always 0
        byte encodingMajor;
        byte encodingMinor;
    }

    [cs:readonly]
    struct ProxyData20
    {
        string? path;                        // Percent-escaped URI path. Null means null proxy.
        Protocol? protocol;                  // null is equivalent to Protocol::Ice2
        string? encoding;                    // null means use the encoding of protocol
        EndpointData? endpoint;
        EndpointDataSeq? altEndpoints;
    }
}
