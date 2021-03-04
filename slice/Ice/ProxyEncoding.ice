//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <Ice/BuiltinSequences.ice>
#include <Ice/Encoding.ice>
#include <Ice/Identity.ice>
#include <Ice/Protocol.ice>

module IceRpc
{
    // These definitions help with the encoding of proxies.

    /// The InvocationMode is carried by proxies that use the ice1 protocol, and it specifies the behavior when sending
    /// a request using such a proxy.
    /// When marshaling an ice1 proxy, IceRPC only uses 3 values: Twoway, Oneway and Datagram.
    enum InvocationMode : byte
    {
        /// This is the default invocation mode; a request using this mode always expects a response.
        Twoway,

        /// A request using oneway mode returns control to the application code as soon as it has been accepted by the
        /// local transport.
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
    ///     - an adapter ID string (renamed location in IceRPC) present only when the sequence of endpoints is empty
    [cs:readonly]
    struct ProxyData11
    {
        StringSeq facetPath;           // has 0 or 1 element
        InvocationMode invocationMode;
        bool secure = false;           // ignored
        Protocol protocol;
        byte protocolMinor = 0;        // always 0
        Encoding encoding;
    }

    /// The kind of proxy being marshaled/unmarshaled (2.0 encoding only).
    enum ProxyKind20 : byte
    {
        /// This optional proxy is null.
        Null,

        /// An ice2 (or greater) ice+transport proxy, where transport corresponds to the transport of the first
        /// endpoint.
        Direct,

        /// An ice2 (or greater) proxy with no endpoint. Its URI scheme is ice.
        Relative,

        /// An ice1 direct proxy
        Ice1Direct,

        /// An Ice1 indirect proxy
        Ice1Indirect
    }

    /// With the 2.0 encoding, a proxy is encoded as a discrimated union with:
    /// - ProxyKind20 (the discriminant)
    /// - if ProxyKind20 is Null: nothing
    /// - if ProxyKind20 is Direct: ProxyData20 followed by a sequence of one or more endpoints
    /// - if ProxyKind20 is Relative: ProxyData20
    /// - if ProxyKind20 is Ice1Direct: Ice1ProxyData20 followed by a sequence of one or more endpoints
    /// - if ProxyKind20 is Ice1Indirect: Ice1ProxyData20 followed by a string (the location), which can be empty

    [cs:readonly]
    struct ProxyData20
    {
        string path;                         // Percent-escaped URI path.
        Protocol? protocol;                  // null is equivalent to Protocol::Ice2
        Encoding? encoding;                  // null is equivalent to Encoding 2.0
    }

    [cs:readonly]
    struct Ice1ProxyData20
    {
        Ice::Identity identity;
        string? facet;                       // null equivalent to ""
        Encoding? encoding;                  // null is equivalent to Encoding 1.1
        InvocationMode? invocationMode;      // null is equivalent to Twoway
    }
}
