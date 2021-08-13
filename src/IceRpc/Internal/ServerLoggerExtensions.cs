// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>This class contains ILogger extension methods for logging Server messages.</summary>
    internal static partial class ServerLoggerExtensions
    {
        [LoggerMessage(
            EventId = (int)ServerEventIds.ServerListening,
            EventName = nameof(ServerEventIds.ServerListening),
            Level = LogLevel.Information,
            Message = "server '{Server}' is listening")]
        internal static partial void LogServerListening(this ILogger logger, Server server);

        [LoggerMessage(
            EventId = (int)ServerEventIds.ServerShuttingDown,
            EventName = nameof(ServerEventIds.ServerShuttingDown),
            Level = LogLevel.Debug,
            Message = "server '{Server}' is shutting down")]
        internal static partial void LogServerShuttingDown(this ILogger logger, Server server);

        [LoggerMessage(
            EventId = (int)ServerEventIds.ServerShutdownComplete,
            EventName = nameof(ServerEventIds.ServerShutdownComplete),
            Level = LogLevel.Information,
            Message = "server '{Server}' completed its shutdown")]
        internal static partial void LogServerShutdownComplete(this ILogger logger, Server server);
    }
}
