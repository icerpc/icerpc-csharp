// Copyright (c) ZeroC, Inc. All rights reserved.

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
}
