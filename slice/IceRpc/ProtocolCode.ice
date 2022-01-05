// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc
{
    /// Represents RPC protocols supported by IceRPC.
    unchecked enum ProtocolCode : byte
    {
        /// The RPC protocol used by ZeroC Ice.
        Ice = 1,

        /// The preferred RPC protocol, based on multiplexed streams.
        IceRpc = 2
    }
}
