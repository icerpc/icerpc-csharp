// Copyright (c) ZeroC, Inc. All rights reserved.

[cs:internal]
module IceRpc::Slice::Internal
{
    // These definitions help with the encoding of proxies with the Ice 2.0 encoding.

    [cs:readonly]
    struct ProxyData20
    {
        protocol: string?,
        path: string?,                        // Percent-escaped URI path. Null means null proxy.
        fragment: string?,                    // Percent-escaped string. Null is equivalent to the empty string.
        encoding: string?,                    // TODO: remove
        params: dictionary<string, string>?,
        endpoint: EndpointData?,
        altEndpoints: sequence<EndpointData>?,
    }
}
