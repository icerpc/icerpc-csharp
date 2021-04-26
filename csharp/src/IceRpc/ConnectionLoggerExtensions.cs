// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace IceRpc
{
    /// <summary>This class contains ILogger extensions methods for logging connection messages.</summary>
    internal static class ConnectionLoggerExtensions
    {
        private const int BaseEventId = LoggerExtensions.ConnectionBaseEventId;
        private const int DispatchException = BaseEventId + 0;
        private const int DispatchCanceledByClient = BaseEventId + 1;

        private static readonly Action<ILogger, Connection, string, string, Exception> _dispatchException =
            LoggerMessage.Define<Connection, string, string>(
                LogLevel.Debug,
                new EventId(DispatchException, nameof(DispatchException)),
                "dispatch exception (Connection={Connection}, Path={Path}, Operation={Operation})");

        private static readonly Action<ILogger, Connection, string, string, Exception> _dispatchCanceledByClient =
            LoggerMessage.Define<Connection, string, string>(
                LogLevel.Debug,
                new EventId(DispatchCanceledByClient, nameof(DispatchCanceledByClient)),
                "dispatch canceled by client (Connection={Connection}, Path={Path}, Operation={Operation})");

        internal static void LogDispatchException(
            this ILogger logger,
            Current current,
            Exception ex) =>
            _dispatchException(logger, current.Connection, current.Path, current.Operation, ex);

        internal static void LogDispatchCanceledByClient(this ILogger logger, Current current) =>
            _dispatchCanceledByClient(logger, current.Connection, current.Path, current.Operation, null!);
    }
}
