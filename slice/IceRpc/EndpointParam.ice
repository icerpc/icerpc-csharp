// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc
{
    /// An endpoint parameter.
    [cs:readonly]
    struct EndpointParam
    {
        /// The parameter name.
        name: string,

        /// The parameter value.
        value: string,
    }
}
