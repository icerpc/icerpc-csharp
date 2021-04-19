// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IceRpc
{
    /// <summary>This class contains ILogger extensions methods for logging Server messages.</summary>
    internal static class ServerLoggerExtensions
    {
        private const int BaseEventId = LoggerExtensions.ServerBaseEventId;
        private const int ServerListening = BaseEventId + 0;
        private const int ServerShuttingDown = BaseEventId + 1;
        private const int ServerShutdownComplete = BaseEventId + 2;
        private const int ServerDispatchException = BaseEventId + 3;
        private const int ServerDispatchCanceledByClient = BaseEventId + 4;

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

        private static readonly Action<ILogger, Server, string, string, Exception> _serverDispatchException =
            LoggerMessage.Define<Server, string, string>(
                LogLevel.Debug,
                new EventId(ServerDispatchException, nameof(ServerDispatchException)),
                "dispatch exception (Server={Server}, Path={Path}, Operation={Operation})");

        private static readonly Action<ILogger, Server, string, string, Exception> _serverDispatchCanceledByClient =
            LoggerMessage.Define<Server, string, string>(
                LogLevel.Debug,
                new EventId(ServerDispatchCanceledByClient, nameof(ServerDispatchCanceledByClient)),
                "dispatch canceled by client (Server={Server}, Path={Path}, Operation={Operation})");

        internal static void LogServerListening(this ILogger logger, Server server) =>
            _serverListening(logger, server, null!);

        internal static void LogServerShuttingDown(this ILogger logger, Server server) =>
            _serverShuttingDown(logger, server, null!);

        internal static void LogServerShutdownComplete(this ILogger logger, Server server) =>
            _serverShutdownComplete(logger, server, null!);

        internal static void LogServerDispatchException(
            this ILogger logger,
            Current current,
            Exception ex) =>
            _serverDispatchException(logger, current.Server, current.Path, current.Operation, ex);

        internal static void LogServerDispatchCanceledByClient(this ILogger logger, Current current) =>
            _serverDispatchCanceledByClient(logger, current.Server, current.Path, current.Operation, null!);
    }
}
