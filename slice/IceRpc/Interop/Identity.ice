//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once

[[suppress-warning(reserved-identifier)]]

[cs:namespace(IceRpc.Interop)]
module Ice
{
    /// The identity of an Ice object.
    [cs:readonly]
    struct Identity
    {
        /// The name of the Ice object. An empty name is not a valid name.
        string name;

        /// The Ice object category.
        string category;
    }

    /// A sequence of identities.
    sequence<Identity> IdentitySeq;
}
