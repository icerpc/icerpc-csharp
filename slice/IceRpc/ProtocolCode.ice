// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc
{
    /// Represents a version of the Ice protocol.
    unchecked enum ProtocolCode : byte
    {
        /// The ice protocol supported by all Ice versions since Ice 1.0.
        Ice1 = 1,
        /// The icerpc protocol introduced in IceRpc.
        Ice2 = 2
    }
}
