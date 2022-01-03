// Copyright (c) ZeroC, Inc. All rights reserved.

module IceRpc
{
    /// Represents RPC protocols supported by IceRPC.
    unchecked enum ProtocolCode : byte
    {
        /// The RPC protocol used by ZeroC Ice.
        Ice = 1,

        /// The default RPC protocol of IceRPC, based on multiplexed streams.
        IceRpc = 2
    }
}
