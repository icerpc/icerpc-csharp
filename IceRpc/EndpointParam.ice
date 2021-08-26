// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

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

    // temporary
    sequence<EndpointParam> EndpointParamSeq;
}
