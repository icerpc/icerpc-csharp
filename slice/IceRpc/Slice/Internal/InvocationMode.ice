// Copyright (c) ZeroC, Inc.

module IceRpc::Slice::Internal
{
    /// A proxy encoded with Slice1 includes an InvocationMode which specifies the behavior when sending requests using
    /// this proxy.
    enum InvocationMode
    {
        /// This is the default invocation mode; a request using this mode always expects a response.
        Twoway,

        /// Not used by IceRPC.
        Oneway,

        /// Not used by IceRPC.
        BatchOneway,

        /// Not used by IceRPC.
        Datagram,

        /// Not used by IceRPC.
        BatchDatagram,
    }
}
