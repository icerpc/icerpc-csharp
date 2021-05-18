// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc
{
    /// <summary>This class contains event ID constants used for server logging.</summary>
    public static class ServerEventIds
    {
        /// <summary>The server starts listening for new connections.</summary>
        /// <seealso cref="Server.Listen"/>
        public static readonly EventId ServerListening = GetEventId(ServerEvent.ServerListening);

        /// <summary>The server shutdown process started.</summary>
        /// <seealso cref="Server.ShutdownAsync(System.Threading.CancellationToken)"/>
        public static readonly EventId ServerShuttingDown = GetEventId(ServerEvent.ServerShuttingDown);

        /// <summary>The server shutdown process completed.</summary>
        public static readonly EventId ServerShutdownComplete = GetEventId(ServerEvent.ServerShutdownComplete);

        private const int BaseEventId = Internal.LoggerExtensions.ServerBaseEventId;

        private enum ServerEvent
        {
            ServerListening = BaseEventId,
            ServerShuttingDown,
            ServerShutdownComplete
        }

        private static EventId GetEventId(ServerEvent e) => new((int)e, e.ToString());
    }
}
