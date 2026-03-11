// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Internal
{
    /// An encoded Ice proxy includes an InvocationMode which specifies the behavior when sending requests using this
    /// proxy.
    ["cs:internal"]
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
