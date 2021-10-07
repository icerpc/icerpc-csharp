// Copyright (c) ZeroC, Inc. All rights reserved.

#pragma once

[[suppress-warning(reserved-identifier)]]

module IceRpc
{
    /// Represents a version of the Ice protocol.
    unchecked enum ProtocolCode : byte
    {
        /// The ice1 protocol supported by all Ice versions since Ice 1.0.
        Ice1 = 1,
        /// The ice2 protocol introduced in IceRpc.
        Ice2 = 2
    }
}
