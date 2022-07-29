// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports;

/// <summary>This enumeration contains event ID constants used for TCP logging.</summary>
public enum TcpEventId
{
    /// <summary>Connect completed successfully.</summary>
    Connect = IceRpc.Internal.BaseEventId.Tcp,

    /// <summary>The TLS authentication operation completed successfully.</summary>
    TlsAuthentication,

    /// <summary>The TSL authentication operation failed.</summary>
    TlsAuthenticationFailed
}
