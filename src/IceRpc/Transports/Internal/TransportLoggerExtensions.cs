// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;

namespace IceRpc.Transports.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging transport messages.</summary>
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
            EventId = (int)TransportEvent.AcceptingConnectionFailed,
            EventName = nameof(TransportEvent.AcceptingConnectionFailed),
            Level = LogLevel.Error,
            Message = "unexpected failure to accept a new connection")]
        internal static partial void LogAcceptingConnectionFailed(this ILogger logger, Exception ex);

        [LoggerMessage(
            EventId = (int)TransportEvent.ConnectionAccepted,
            EventName = nameof(TransportEvent.ConnectionAccepted),
            Level = LogLevel.Debug,
            Message = "accepted connection")]
        internal static partial void LogConnectionAccepted(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEvent.ConnectionAcceptFailed,
            EventName = nameof(TransportEvent.ConnectionAcceptFailed),
            Level = LogLevel.Debug,
            Message = "failed to accept connection")]
        internal static partial void LogConnectionAcceptFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = (int)TransportEvent.ConnectionClosed,
            EventName = nameof(TransportEvent.ConnectionClosed),
            Level = LogLevel.Debug,
            Message = "closed connection (Reason={Reason})")]
        internal static partial void LogConnectionClosed(
            this ILogger logger,
            string reason,
            Exception? exception = null);

        [LoggerMessage(
            EventId = (int)TransportEvent.ConnectionConnectFailed,
            EventName = nameof(TransportEvent.ConnectionConnectFailed),
            Level = LogLevel.Debug,
            Message = "connection establishment failed")]
        internal static partial void LogConnectionConnectFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = (int)TransportEvent.ConnectionEstablished,
            EventName = nameof(TransportEvent.ConnectionEstablished),
            Level = LogLevel.Debug,
            Message = "established connection")]
        internal static partial void LogConnectionEstablished(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEvent.ConnectionEventHandlerException,
            EventName = nameof(TransportEvent.ConnectionEventHandlerException),
            Level = LogLevel.Warning,
            Message = "{Name} event handler raised exception")]
        internal static partial void LogConnectionEventHandlerException(this ILogger logger, string name, Exception ex);

        [LoggerMessage(
            EventId = (int)TransportEvent.ReceiveBufferSizeAdjusted,
            EventName = nameof(TransportEvent.ReceiveBufferSizeAdjusted),
            Level = LogLevel.Debug,
            Message = "{Transport} receive buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}")]
        internal static partial void LogReceiveBufferSizeAdjusted(
            this ILogger logger,
            Transport transport,
            int requestedSize,
            int adjustedSize);

        [LoggerMessage(
            EventId = (int)TransportEvent.ReceivedData,
            EventName = nameof(TransportEvent.ReceivedData),
            Level = LogLevel.Trace,
            Message = "received {Size} bytes ({Data})")]
        internal static partial void LogReceivedData(this ILogger logger, int size, string data);

        [LoggerMessage(
            EventId = (int)TransportEvent.ReceivedInvalidDatagram,
            EventName = nameof(TransportEvent.ReceivedInvalidDatagram),
            Level = LogLevel.Debug,
            Message = "received invalid {Bytes} bytes datagram")]
        internal static partial void LogReceivedInvalidDatagram(this ILogger logger, int bytes);

        [LoggerMessage(
            EventId = (int)TransportEvent.SendBufferSizeAdjusted,
            EventName = nameof(TransportEvent.SendBufferSizeAdjusted),
            Level = LogLevel.Debug,
            Message = "{Transport} send buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}")]
        internal static partial void LogSendBufferSizeAdjusted(
            this ILogger logger,
            Transport transport,
            int requestedSize,
            int adjustedSize);

        [LoggerMessage(
            EventId = (int)TransportEvent.SentData,
            EventName = nameof(TransportEvent.SentData),
            Level = LogLevel.Trace,
            Message = "sent {Size} bytes ({Data})")]
        internal static partial void LogSentData(this ILogger logger, int size, string data);

        [LoggerMessage(
           EventId = (int)TransportEvent.StartAcceptingConnections,
           EventName = nameof(TransportEvent.StartAcceptingConnections),
           Level = LogLevel.Information,
           Message = "starting to accept connections")]
        internal static partial void LogStartAcceptingConnections(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEvent.StartReceivingDatagrams,
            EventName = nameof(TransportEvent.StartReceivingDatagrams),
            Level = LogLevel.Information,
            Message = "starting to receive datagrams")]
        internal static partial void LogStartReceivingDatagrams(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEvent.StartReceivingDatagramsFailed,
            EventName = nameof(TransportEvent.StartReceivingDatagramsFailed),
            Level = LogLevel.Information,
            Message = "starting receiving datagrams failed")]
        internal static partial void LogStartReceivingDatagramsFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = (int)TransportEvent.StartSendingDatagrams,
            EventName = nameof(TransportEvent.StartSendingDatagrams),
            Level = LogLevel.Debug,
            Message = "starting to send datagrams")]
        internal static partial void LogStartSendingDatagrams(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEvent.StartSendingDatagramsFailed,
            EventName = nameof(TransportEvent.StartSendingDatagramsFailed),
            Level = LogLevel.Debug,
            Message = "starting sending datagrams failed")]
        internal static partial void LogStartSendingDatagramsFailed(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = (int)TransportEvent.StopAcceptingConnections,
            EventName = nameof(TransportEvent.StopAcceptingConnections),
            Level = LogLevel.Information,
            Message = "stopping to accept connections")]
        internal static partial void LogStopAcceptingConnections(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEvent.StopReceivingDatagrams,
            EventName = nameof(TransportEvent.StopReceivingDatagrams),
            Level = LogLevel.Information,
            Message = "stopping to receive datagrams")]
        internal static partial void LogStopReceivingDatagrams(this ILogger logger);

        internal static IDisposable? StartServerScope(this ILogger logger, IListener listener)
        {
            if (!logger.IsEnabled(LogLevel.Error))
            {
                return null;
            }

            return _listenerScope(logger, listener.Endpoint.ToString());
        }

        internal static IDisposable? StartConnectionScope(this ILogger logger, Connection connection)
        {
            if (!logger.IsEnabled(LogLevel.Error))
            {
                return null;
            }

            if (connection.IsServer)
            {
                string? remoteEndpoint = null;
                if (connection.State > ConnectionState.NotConnected)
                {
                    try
                    {
                        remoteEndpoint = connection.RemoteEndpoint?.ToString() ?? "undefined";
                    }
                    catch
                    {
                    }
                }
                return _serverConnectionScope(logger, remoteEndpoint ?? "not connected");
            }
            else
            {
                string? localEndpoint = null;
                if (connection.State > ConnectionState.NotConnected)
                {
                    try
                    {
                        localEndpoint = connection.LocalEndpoint?.ToString() ?? "undefined";
                    }
                    catch
                    {
                    }
                }

                string? remoteEndpoint = null;
                if (connection.State > ConnectionState.NotConnected)
                {
                    try
                    {
                        remoteEndpoint = connection.RemoteEndpoint?.ToString() ?? "undefined";
                    }
                    catch
                    {
                    }
                }

                return _clientConnectionScope(
                    logger,
                    localEndpoint ?? "not connected",
                    remoteEndpoint ?? "not connected");
            }
        }

        internal static IDisposable? StartStreamScope(this ILogger logger, long id)
        {
            if (!logger.IsEnabled(LogLevel.Error))
            {
                return null;
            }

            (string initiatedBy, string kind) = (id % 4) switch
            {
                0 => ("Client", "Bidirectional"),
                1 => ("Server", "Bidirectional"),
                2 => ("Client", "Unidirectional"),
                _ => ("Server", "Unidirectional")
            };
            return _streamScope(logger, id, initiatedBy, kind);
        }
    }
}
