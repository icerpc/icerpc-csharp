// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    /// <summary>This class contains ILogger extension methods for logging calls to the transport APIs.</summary>
    internal static partial class TransportLoggerExtensions
    {
        private static readonly Func<ILogger, long, string, string, IDisposable> _streamScope =
            LoggerMessage.DefineScope<long, string, string>("stream(ID={ID}, InitiatedBy={InitiatedBy}, Kind={Kind})");

        [LoggerMessage(
            EventId = (int)TransportEventIds.Connect,
            EventName = nameof(TransportEventIds.Connect),
            Level = LogLevel.Debug,
            Message = "connect completed successfully")]
        internal static partial void LogConnect(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectFailed,
            EventName = nameof(TransportEventIds.ConnectFailed),
            Level = LogLevel.Debug,
            Message = "connect failed")]
        internal static partial void LogConnectFailed(this ILogger logger, Exception? exception);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ListenerAcceptConnectionFailed,
            EventName = nameof(TransportEventIds.ListenerAcceptConnectionFailed),
            Level = LogLevel.Error,
            Message = "server `{endpoint}' failed to accept a new connection")]
        internal static partial void LogListenerAcceptingConnectionFailed(
            this ILogger logger,
            Endpoint endpoint,
            Exception ex);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ListenerListening,
            EventName = nameof(TransportEventIds.ListenerListening),
            Level = LogLevel.Information,
            Message = "server '{endpoint}' is listening")]
        internal static partial void LogListenerListening(this ILogger logger, Endpoint endpoint);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ListenerShutDown,
            EventName = nameof(TransportEventIds.ListenerShutDown),
            Level = LogLevel.Debug,
            Message = "server '{endpoint}' is no longer accepting connections")]
        internal static partial void LogListenerShutDown(this ILogger logger, Endpoint endpoint);

        [LoggerMessage(
            EventId = (int)TransportEventIds.StreamRead,
            EventName = nameof(TransportEventIds.StreamRead),
            Level = LogLevel.Trace,
            Message = "read {Size} bytes ({Data}) from stream")]
        internal static partial void LogStreamRead(this ILogger logger, int size, string data);

        [LoggerMessage(
            EventId = (int)TransportEventIds.StreamWrite,
            EventName = nameof(TransportEventIds.StreamWrite),
            Level = LogLevel.Trace,
            Message = "wrote {Size} bytes ({Data}) to stream")]
        internal static partial void LogStreamWrite(this ILogger logger, int size, string data);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectionDispose,
            EventName = nameof(TransportEventIds.ConnectionDispose),
            Level = LogLevel.Debug,
            Message = "connection closed")]
        internal static partial void LogConnectionDispose(this ILogger logger);

        internal static IDisposable StartStreamScope(this ILogger logger, long id) =>
            (id % 4) switch
            {
                0 => _streamScope(logger, id, "Client", "Bidirectional"),
                1 => _streamScope(logger, id, "Server", "Bidirectional"),
                2 => _streamScope(logger, id, "Client", "Unidirectional"),
                _ => _streamScope(logger, id, "Server", "Unidirectional")
            };
    }
}
