// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace IceRpc.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging connection messages.</summary>
    internal static class ConnectionLoggerExtensions
    {
        private static readonly Action<ILogger, Connection, string, string, Exception> _dispatchException =
            LoggerMessage.Define<Connection, string, string>(
                LogLevel.Debug,
                ConnectionEventIds.DispatchException,
                "dispatch exception (Connection={Connection}, Path={Path}, Operation={Operation})");

        private static readonly Action<ILogger, Connection, string, string, Exception> _dispatchCanceledByClient =
            LoggerMessage.Define<Connection, string, string>(
                LogLevel.Debug,
                ConnectionEventIds.DispatchCanceledByClient,
                "dispatch canceled by client (Connection={Connection}, Path={Path}, Operation={Operation})");

        internal static void LogDispatchException(
            this ILogger logger,
            IncomingRequest request,
            Exception ex) =>
            _dispatchException(logger, request.Connection, request.Path, request.Operation, ex);

        internal static void LogDispatchCanceledByClient(this ILogger logger, IncomingRequest request) =>
            _dispatchCanceledByClient(logger, request.Connection, request.Path, request.Operation, null!);
    }
}
