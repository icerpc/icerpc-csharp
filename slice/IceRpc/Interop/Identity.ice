// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

#include <IceRpc/BuiltinSequences.ice>

[[suppress-warning(reserved-identifier)]]

[cs:namespace(IceRpc.Interop)]
module Ice
{
    /// The identity of a service reachable through the ice1 protocol.
    [cs:readonly]
    struct Identity
    {
        /// The name component of the identity. An empty name is not a valid name.
        string name;

        /// The category of the identity. Can be empty.
        string category;
    }

     /// The identity and facet of a service reachable through the ice1 protocol. They both map to path with the ice2
     /// protocol. For historical reasons, the facet is represented by a facet path.
    [cs:readonly]
    struct IdentityAndFacet
    {
        /// The identity.
        Identity identity;

        /// The facet path, with 0 or 1 elements.
        IceRpc::StringSeq facetPath;
    }
}
