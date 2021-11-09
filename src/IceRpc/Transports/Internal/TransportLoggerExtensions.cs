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
            Message = "connect completed successfully: LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint}")]
        internal static partial void LogConnect(this ILogger logger, Endpoint localEndpoint, Endpoint remoteEndpoint);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectFailed,
            EventName = nameof(TransportEventIds.ConnectFailed),
            Level = LogLevel.Debug,
            Message = "connect failed")]
        internal static partial void LogConnectFailed(this ILogger logger, Exception? exception);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ListenerAcceptFailed,
            EventName = nameof(TransportEventIds.ListenerAcceptFailed),
            Level = LogLevel.Error,
            Message = "listener '{endpoint}' failed to accept a new connection")]
        internal static partial void LogListenerAcceptFailed(
            this ILogger logger,
            Endpoint endpoint,
            Exception ex);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ListenerCreated,
            EventName = nameof(TransportEventIds.ListenerCreated),
            Level = LogLevel.Information,
            Message = "listener '{endpoint}' started")]
        internal static partial void LogListenerCreated(this ILogger logger, Endpoint endpoint);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ListenerDisposed,
            EventName = nameof(TransportEventIds.ListenerDisposed),
            Level = LogLevel.Debug,
            Message = "listener '{endpoint}' shut down")]
        internal static partial void LogListenerDispose(this ILogger logger, Endpoint endpoint);

        [LoggerMessage(
            EventId = (int)TransportEventIds.MultiplexedStreamRead,
            EventName = nameof(TransportEventIds.MultiplexedStreamRead),
            Level = LogLevel.Trace,
            Message = "read {Size} bytes from multiplexed stream ({Data})")]
        internal static partial void LogMultiplexedStreamRead(
            this ILogger logger,
            int size,
            string data);

        [LoggerMessage(
            EventId = (int)TransportEventIds.MultiplexedStreamWrite,
            EventName = nameof(TransportEventIds.MultiplexedStreamWrite),
            Level = LogLevel.Trace,
            Message = "wrote {Size} bytes to multiplexed stream ({Data})")]
        internal static partial void LogMultiplexedStreamWrite(
            this ILogger logger,
            int size,
            string data);

        [LoggerMessage(
            EventId = (int)TransportEventIds.SimpleStreamRead,
            EventName = nameof(TransportEventIds.SimpleStreamRead),
            Level = LogLevel.Trace,
            Message = "read {Size} bytes from simple stream ({Data})")]
        internal static partial void LogSimpleStreamRead(this ILogger logger, int size, string data);

        [LoggerMessage(
            EventId = (int)TransportEventIds.SimpleStreamWrite,
            EventName = nameof(TransportEventIds.SimpleStreamWrite),
            Level = LogLevel.Trace,
            Message = "wrote {Size} bytes to simple stream ({Data})")]
        internal static partial void LogSimpleStreamWrite(this ILogger logger, int size, string data);

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
