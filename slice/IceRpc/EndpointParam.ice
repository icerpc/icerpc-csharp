// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc
{
    /// An endpoint parameter.
    [cs:readonly]
    struct EndpointParam
    {
        /// The parameter name.
        string name;

        /// The parameter value.
        string value;
    }
}
