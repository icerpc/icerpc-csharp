// Copyright (c) ZeroC, Inc.

module IceRpc::Ice::Internal
{
    /// Represents the body of a tcp or ssl server address in an Ice proxy.
    ["cs:internal"]
    ["cs:readonly"]
    struct TcpServerAddressBody
    {
        string host;
        int port;
        int timeout;
        bool compress;
    }
}
