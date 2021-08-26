// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <IceRpc/Slice/EndpointData.ice>
#include <IceRpc/Protocol.ice>

module IceRpc::Slice
{
    // These definitions help with the encoding of proxies with the Ice 2.0 encoding.

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
