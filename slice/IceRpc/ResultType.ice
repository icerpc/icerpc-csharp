// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc
{
    /// The type of result carried by an ice2 response frame.
    enum ResultType : byte
    {
        /// The request succeeded.
        Success = 0,

        /// The request failed.
        Failure = 1
    }
}
