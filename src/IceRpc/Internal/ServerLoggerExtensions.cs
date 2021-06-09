// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging Server messages.</summary>
    internal static partial class ServerLoggerExtensions
    {
        [LoggerMessage(
            EventId = (int)ServerEvent.ServerListening,
            EventName = nameof(ServerEvent.ServerListening),
            Level = LogLevel.Information,
            Message = "server '{Server}' is listening")]
        internal static partial void LogServerListening(this ILogger logger, Server server);

        [LoggerMessage(
            EventId = (int)ServerEvent.ServerShuttingDown,
            EventName = nameof(ServerEvent.ServerShuttingDown),
            Level = LogLevel.Debug,
            Message = "server '{Server}' is shutting down")]
        internal static partial void LogServerShuttingDown(this ILogger logger, Server server);

        [LoggerMessage(
            EventId = (int)ServerEvent.ServerShutdownComplete,
            EventName = nameof(ServerEvent.ServerShutdownComplete),
            Level = LogLevel.Information,
            Message = "server '{Server}' completed its shutdown")]
        internal static partial void LogServerShutdownComplete(this ILogger logger, Server server);
    }
}
