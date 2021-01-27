//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

#include <Ice/BuiltinSequences.ice>

[cs:namespace(ZeroC)]
module Ice
{
    /// The Ice router interface. Routers can be set either globally with
    /// {@link Communicator#setDefaultRouter}, or with <code>ice_router</code> on specific
    /// proxies.
    interface Router
    {
        /// Get the router's client proxy, i.e., the proxy to use for
        /// forwarding requests from the client to the router.
        ///
        /// If a null proxy is returned, the client will forward requests
        /// to the router's endpoints.
        ///
        /// @return A tuple containing the router's client proxy and whether or not the router supports a routing table.
        ///         If it is supported, the Ice runtime will call addProxies to populate the routing table.
        ///         The Ice runtime assumes the router has a routing table if the optional is not set.
        [nonmutating] idempotent (Object? clientProxy, tag(1) bool? hasRoutingTable) getClientProxy();

        /// Get the router's server proxy, i.e., the proxy to use for
        /// forwarding requests from the server to the router.
        ///
        /// @return The router's server proxy.
        [nonmutating] idempotent Object? getServerProxy();

        /// Add new proxy information to the router's routing table.
        ///
        /// @param proxies The proxies to add.
        ///
        /// @return Proxies discarded by the router.
        idempotent ProxySeq addProxies(ProxySeq proxies);
    }

    /// This interface should be implemented by services implementing the
    /// Ice::Router interface. It should be advertised through an Ice
    /// object with the identity `Ice/RouterFinder'. This allows clients to
    /// retrieve the router proxy with just the endpoint information of the
    /// service.
    interface RouterFinder
    {
        /// Get the router proxy implemented by the process hosting this
        /// finder object. The proxy might point to several replicas.
        ///
        /// @return The router proxy.
        Router? getRouter();
    }
}
