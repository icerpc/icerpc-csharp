// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc.Transports
{
    /// <summary>This class contains event ID constants used for Tls logging.</summary>
    public enum TlsEvent
    {
        /// <summary>The TLS authentication operation completed successfully.</summary>
        TlsAuthenticationSucceeded = IceRpc.Internal.LoggerExtensions.TlsBaseEventId,
        /// <summary>The TSL authentication operation failed.</summary>
        TlsAuthenticationFailed
    }
}
