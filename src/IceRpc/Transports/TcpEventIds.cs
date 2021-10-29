// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>This class contains event ID constants used for TCP logging.</summary>
    public enum TcpEventIds
    {
        /// <summary>The server accepted a new connection.</summary>
        ConnectionAccepted = IceRpc.Internal.BaseEventIds.Tcp,

        /// <summary>The client connection was established.</summary>
        ConnectionEstablished,

        /// <summary>The TLS authentication operation completed successfully.</summary>
        TlsAuthenticationSucceeded,

        /// <summary>The TSL authentication operation failed.</summary>
        TlsAuthenticationFailed
    }
}
