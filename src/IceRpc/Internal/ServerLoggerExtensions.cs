// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace IceRpc.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging Server messages.</summary>
    internal static class ServerLoggerExtensions
    {
        private static readonly Action<ILogger, Server, Exception> _serverListening =
            LoggerMessage.Define<Server>(
                LogLevel.Information,
                ServerEventIds.ServerListening,
                "server '{Name}' is listening");

        private static readonly Action<ILogger, Server, Exception> _serverShuttingDown =
            LoggerMessage.Define<Server>(
                LogLevel.Debug,
                ServerEventIds.ServerShuttingDown,
                "server '{Name}' is shutting down");

        private static readonly Action<ILogger, Server, Exception> _serverShutdownComplete =
            LoggerMessage.Define<Server>(
                LogLevel.Information,
                ServerEventIds.ServerShutdownComplete,
                "server '{Name}' completed its shutdown");

        internal static void LogServerListening(this ILogger logger, Server server) =>
            _serverListening(logger, server, null!);

        internal static void LogServerShuttingDown(this ILogger logger, Server server) =>
            _serverShuttingDown(logger, server, null!);

        internal static void LogServerShutdownComplete(this ILogger logger, Server server) =>
            _serverShutdownComplete(logger, server, null!);
    }
}
