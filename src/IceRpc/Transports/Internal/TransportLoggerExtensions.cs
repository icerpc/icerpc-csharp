// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    /// <summary>This class contains ILogger extension methods for logging transport messages.</summary>
    internal static partial class TransportLoggerExtensions
    {
        private static readonly Func<ILogger, string, IDisposable> _listenerScope =
            LoggerMessage.DefineScope<string>("server(Endpoint={Server})");

        private static readonly Func<ILogger, string, IDisposable> _serverConnectionScope =
            LoggerMessage.DefineScope<string>("connection(RemoteEndpoint={RemoteEndpoint})");

        private static readonly Func<ILogger, string, string, IDisposable> _clientConnectionScope =
            LoggerMessage.DefineScope<string, string>(
                "connection(LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint})");

        private static readonly Func<ILogger, long, string, string, IDisposable> _streamScope =
            LoggerMessage.DefineScope<long, string, string>("stream(ID={ID}, InitiatedBy={InitiatedBy}, Kind={Kind})");

        [LoggerMessage(
            EventId = (int)TransportEventIds.AcceptingConnectionFailed,
            EventName = nameof(TransportEventIds.AcceptingConnectionFailed),
            Level = LogLevel.Error,
            Message = "unexpected failure to accept a new connection")]
        internal static partial void LogAcceptingConnectionFailed(this ILogger logger, Exception ex);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectionAccepted,
            EventName = nameof(TransportEventIds.ConnectionAccepted),
            Level = LogLevel.Debug,
            Message = "accepted connection")]
        internal static partial void LogConnectionAccepted(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectionAcceptFailed,
            EventName = nameof(TransportEventIds.ConnectionAcceptFailed),
            Level = LogLevel.Debug,
            Message = "failed to accept connection")]
        internal static partial void LogConnectionAcceptFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectionClosed,
            EventName = nameof(TransportEventIds.ConnectionClosed),
            Level = LogLevel.Debug,
            Message = "closed connection (Reason={Reason})")]
        internal static partial void LogConnectionClosed(
            this ILogger logger,
            string reason,
            Exception? exception = null);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectionConnectFailed,
            EventName = nameof(TransportEventIds.ConnectionConnectFailed),
            Level = LogLevel.Debug,
            Message = "connection establishment failed")]
        internal static partial void LogConnectionConnectFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectionEstablished,
            EventName = nameof(TransportEventIds.ConnectionEstablished),
            Level = LogLevel.Debug,
            Message = "established connection")]
        internal static partial void LogConnectionEstablished(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectionEventHandlerException,
            EventName = nameof(TransportEventIds.ConnectionEventHandlerException),
            Level = LogLevel.Warning,
            Message = "{Name} event handler raised exception")]
        internal static partial void LogConnectionEventHandlerException(this ILogger logger, string name, Exception ex);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ReceiveBufferSizeAdjusted,
            EventName = nameof(TransportEventIds.ReceiveBufferSizeAdjusted),
            Level = LogLevel.Debug,
            Message = "{Transport} receive buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}")]
        internal static partial void LogReceiveBufferSizeAdjusted(
            this ILogger logger,
            string transport,
            int requestedSize,
            int adjustedSize);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ReceivedData,
            EventName = nameof(TransportEventIds.ReceivedData),
            Level = LogLevel.Trace,
            Message = "received {Size} bytes ({Data})")]
        internal static partial void LogReceivedData(this ILogger logger, int size, string data);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ReceivedInvalidDatagram,
            EventName = nameof(TransportEventIds.ReceivedInvalidDatagram),
            Level = LogLevel.Debug,
            Message = "received invalid {Bytes} bytes datagram")]
        internal static partial void LogReceivedInvalidDatagram(this ILogger logger, int bytes);

        [LoggerMessage(
            EventId = (int)TransportEventIds.SendBufferSizeAdjusted,
            EventName = nameof(TransportEventIds.SendBufferSizeAdjusted),
            Level = LogLevel.Debug,
            Message = "{Transport} send buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}")]
        internal static partial void LogSendBufferSizeAdjusted(
            this ILogger logger,
            string transport,
            int requestedSize,
            int adjustedSize);

        [LoggerMessage(
            EventId = (int)TransportEventIds.SentData,
            EventName = nameof(TransportEventIds.SentData),
            Level = LogLevel.Trace,
            Message = "sent {Size} bytes ({Data})")]
        internal static partial void LogSentData(this ILogger logger, int size, string data);

        [LoggerMessage(
           EventId = (int)TransportEventIds.StartAcceptingConnections,
           EventName = nameof(TransportEventIds.StartAcceptingConnections),
           Level = LogLevel.Information,
           Message = "starting to accept connections")]
        internal static partial void LogStartAcceptingConnections(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEventIds.StartReceivingDatagrams,
            EventName = nameof(TransportEventIds.StartReceivingDatagrams),
            Level = LogLevel.Information,
            Message = "starting to receive datagrams")]
        internal static partial void LogStartReceivingDatagrams(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEventIds.StartReceivingDatagramsFailed,
            EventName = nameof(TransportEventIds.StartReceivingDatagramsFailed),
            Level = LogLevel.Information,
            Message = "starting receiving datagrams failed")]
        internal static partial void LogStartReceivingDatagramsFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = (int)TransportEventIds.StartSendingDatagrams,
            EventName = nameof(TransportEventIds.StartSendingDatagrams),
            Level = LogLevel.Debug,
            Message = "starting to send datagrams")]
        internal static partial void LogStartSendingDatagrams(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEventIds.StartSendingDatagramsFailed,
            EventName = nameof(TransportEventIds.StartSendingDatagramsFailed),
            Level = LogLevel.Debug,
            Message = "starting sending datagrams failed")]
        internal static partial void LogStartSendingDatagramsFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = (int)TransportEventIds.StopAcceptingConnections,
            EventName = nameof(TransportEventIds.StopAcceptingConnections),
            Level = LogLevel.Information,
            Message = "stopping to accept connections")]
        internal static partial void LogStopAcceptingConnections(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEventIds.StopReceivingDatagrams,
            EventName = nameof(TransportEventIds.StopReceivingDatagrams),
            Level = LogLevel.Information,
            Message = "stopping to receive datagrams")]
        internal static partial void LogStopReceivingDatagrams(this ILogger logger);

        internal static IDisposable? StartServerScope(this ILogger logger, IListener listener)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                return _listenerScope(logger, listener.Endpoint.ToString());
            }
            else
            {
                return null;
            }
        }

        internal static IDisposable? StartConnectionScope(this ILogger logger, Connection connection)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                if (connection.IsServer)
                {
                    return _serverConnectionScope(logger, connection.RemoteEndpoint?.ToString() ?? "undefined");
                }
                else
                {
                    return _clientConnectionScope(
                        logger,
                        connection.LocalEndpoint?.ToString() ?? "undefined",
                        connection.RemoteEndpoint?.ToString() ?? "undefined");
                }
            }
            else
            {
                return null;
            }
        }

        internal static IDisposable? StartStreamScope(this ILogger logger, long id)
        {
            if (logger.IsEnabled(LogLevel.Error))
            {
                (string initiatedBy, string kind) = (id % 4) switch
                {
                    0 => ("Client", "Bidirectional"),
                    1 => ("Server", "Bidirectional"),
                    2 => ("Client", "Unidirectional"),
                    _ => ("Server", "Unidirectional")
                };
                return _streamScope(logger, id, initiatedBy, kind);
            }
            else
            {
                return null;
            }
        }
    }
}
