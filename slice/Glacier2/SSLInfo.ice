//
// Copyright (c) ZeroC, Inc. All rights reserved.
//

#pragma once



#include <Ice/BuiltinSequences.ice>

[cs:namespace(ZeroC)]
module Glacier2
{
    /// Information taken from an SSL connection used for permissions
    /// verification.
    ///
    /// @see PermissionsVerifier
    struct SSLInfo
    {
        ///The remote host.
        string remoteHost;

        ///The remote port.
        int remotePort;

        ///The router's host.
        string localHost;

        ///The router's port.
        int localPort;

        ///The negotiated cipher suite.
        string cipher;

        ///The certificate chain.
        Ice::StringSeq certs;
    }
}
