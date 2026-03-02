// Copyright (c) ZeroC, Inc.

module IceRpc::Slice::Internal
{
    /// Represents the body of a tcp or ssl server address in an ice proxy.
    struct TcpServerAddressBody
    {
        string host;
        int port;
        int timeout;
        bool compress;
    }
}
