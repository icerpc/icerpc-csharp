// Copyright (c) ZeroC, Inc. All rights reserved.

[cs:internal]
module IceRpc::Slice::Internal
{
    // These definitions help with the encoding of proxies with the Ice 2.0 encoding.

    [cs:readonly]
    struct ProxyData20
    {
        path: string?,                        // Percent-escaped URI path. Null means null proxy.
        fragment: string?,                    // Percent-escaped string. Null is equivalent to the empty string.
        protocol: ProtocolCode?,
        encoding: string?,                    // TODO: remove
        endpoint: EndpointData?,
        altEndpoints: sequence<EndpointData>?,
    }
}
