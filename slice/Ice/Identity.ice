// Copyright (c) ZeroC, Inc. All rights reserved.

[[suppress-warning(reserved-identifier)]]

[cs:namespace(IceRpc)]
module Ice
{
    /// The identity of a service reachable with the ice1 protocol.
    [cs:readonly]
    struct Identity
    {
        /// The name of the identity. An empty name is not a valid name.
        string name;

        /// The category of the identity. Can be empty.
        string category;
    }

     /// The identity and facet of a service reachable with the ice1 protocol. They both map to path with the ice2
     /// protocol.
    [cs:readonly]
    struct IdentityAndFacet
    {
        /// The identity.
        Identity identity;

        /// The optional facet: an empty sequence represents a null facet, while a single element represents a non-null
        /// facet. However, a null facet and a non-null empty facet are always treated the same.
        sequence<string> optionalFacet;
    }
}
