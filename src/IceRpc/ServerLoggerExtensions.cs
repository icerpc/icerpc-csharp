// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace IceRpc
{
    /// <summary>This class contains ILogger extensions methods for logging Server messages.</summary>
    internal static class ServerLoggerExtensions
    {
        private const int BaseEventId = LoggerExtensions.ServerBaseEventId;
        private const int ServerListening = BaseEventId + 0;
        private const int ServerShuttingDown = BaseEventId + 1;
        private const int ServerShutdownComplete = BaseEventId + 2;

        private static readonly Action<ILogger, Server, Exception> _serverListening =
            LoggerMessage.Define<Server>(
                LogLevel.Information,
                new EventId(ServerListening, nameof(ServerListening)),
                "server '{Name}' is listening");

        private static readonly Action<ILogger, Server, Exception> _serverShuttingDown =
            LoggerMessage.Define<Server>(
                LogLevel.Debug,
                new EventId(ServerShuttingDown, nameof(ServerShuttingDown)),
                "server '{Name}' is shutting down");

        private static readonly Action<ILogger, Server, Exception> _serverShutdownComplete =
            LoggerMessage.Define<Server>(
                LogLevel.Information,
                new EventId(ServerShutdownComplete, nameof(ServerShutdownComplete)),
                "server '{Name}' completed its shutdown");

        internal static void LogServerListening(this ILogger logger, Server server) =>
            _serverListening(logger, server, null!);

        internal static void LogServerShuttingDown(this ILogger logger, Server server) =>
            _serverShuttingDown(logger, server, null!);

        internal static void LogServerShutdownComplete(this ILogger logger, Server server) =>
            _serverShutdownComplete(logger, server, null!);
    }
}
