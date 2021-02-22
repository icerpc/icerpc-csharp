//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <Ice/BuiltinSequences.ice>
#include <Ice/Encoding.ice>
#include <Ice/Identity.ice>
#include <Ice/Protocol.ice>

[cs:namespace(ZeroC)]
module Ice
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
    ///     - an adapter ID string (renamed location in Ice 4.0) present only when the sequence of endpoints is empty
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

        /// A proxy with one or more endpoints; with ice2, its URI scheme is ice+transport, where transport corresponds
        /// to the transport of the first endpoint.
        Direct,

        /// A proxy with no endpoint: for ice2, the URI scheme is ice. With ice1, Relative maps to an indirect proxy.
        Relative
    }

    /// With the 2.0 encoding, a proxy is encoded as a discrimated union with:
    /// - ProxyKind20 (the discriminant)
    /// - if ProxyKind20 is not Null:
    ///    - ProxyData20
    ///    - If ProxyKind20 is Direct, a sequence of one or more endpoints
    [cs:readonly]
    struct ProxyData20
    {
        Identity identity;
        Protocol? protocol;                  // null is equivalent to Protocol::Ice2
        Encoding? encoding;                  // null is equivalent to Encoding 2.0
        StringSeq? location;                 // null is equivalent to an empty sequence
        InvocationMode? invocationMode;      // always null when protocol != Protocol.Ice1
        string? facet;                       // null equivalent to ""
    }
}
