// Copyright (c) ZeroC, Inc. All rights reserved.

[cs:internal]
module IceRpc::Slice::Internal
{
    // These definitions help with the encoding of proxies with the Ice 2.0 encoding.

    [cs:readonly]
    struct ProxyData20
    {
        string? path;                        // Percent-escaped URI path. Null means null proxy.
        string? fragment;                    // null is equivalent to the empty string.
        ProtocolCode? protocol;
        string? encoding;                    // TODO: remove
        EndpointData? endpoint;
        sequence<EndpointData>? altEndpoints;
    }
}
