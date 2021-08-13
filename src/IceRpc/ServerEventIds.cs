// Copyright (c) ZeroC, Inc. All rights reserved.

namespace IceRpc
{
    /// <summary>This enum contains event ID constants used for server logging.</summary>
    public enum ServerEventIds
    {
        /// <summary>The server starts listening for new connections.</summary>
        /// <seealso cref="Server.Listen"/>
        ServerListening = Internal.BaseEventIds.Server,
        /// <summary>The server shutdown process started.</summary>
        /// <seealso cref="Server.ShutdownAsync(System.Threading.CancellationToken)"/>
        ServerShuttingDown,
        /// <summary>The server shutdown process completed.</summary>
        ServerShutdownComplete
    }
}
