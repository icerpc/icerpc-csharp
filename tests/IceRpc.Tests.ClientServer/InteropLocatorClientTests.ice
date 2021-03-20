// Copyright (c) ZeroC, Inc. All rights reserved.

#include <IceRpc/Interop/Identity.ice>
#include <IceRpc/Interop/Locator.ice>

[[suppress-warning(reserved-identifier)]]

module IceRpc::Tests::ClientServer
{
    interface SimpleLocatorTest : Ice::Locator
    {
        // With the 1.1 encoding, the only way to marshal endpoints (here direct endpoints) is as part of a dummy proxy.
        void registerAdapter(string adapter, Object dummy);

        // For a well-known proxy aka identity, the endpoint(s) can be direct or loc.
        void registerWellKnownProxy(Ice::Identity identity, Object dummy);

        // Returns true when found, otherwise false
        bool unregisterAdapter(string adapter);

        bool unregisterWellKnownProxy(Ice::Identity identity);
    }
}
