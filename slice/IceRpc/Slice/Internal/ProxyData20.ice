// Copyright (c) ZeroC, Inc. All rights reserved.

[cs:internal]
module IceRpc::Slice::Internal
{
    // These definitions help with the encoding of proxies with the Ice 2.0 encoding.

    [cs:readonly]
    struct ProxyData20
    {
        string? path;                        // Percent-escaped URI path. Null means null proxy.
        ProtocolCode? protocol;              // null is equivalent to ProtocolCode::Ice2
        string? encoding;                    // null means use the encoding of protocol
        EndpointData? endpoint;
        sequence<EndpointData>? altEndpoints;
    }
}
