// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Net;

namespace IceRpc.Transports.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging transport messages.</summary>
    internal static partial class TransportLoggerExtensions
    {
        private static readonly Func<ILogger, string, Protocol, string, string, IDisposable> _listenerScope =
            LoggerMessage.DefineScope<string, Protocol, string, string>(
                "server(Transport={Transport}, Protocol={Protocol}, Server={Server}, Description={Description})");

        private static readonly Func<ILogger, string, Protocol, string, IDisposable> _clientConnectionScope =
            LoggerMessage.DefineScope<string, Protocol, string>(
                "connection(Transport={Transport}, Protocol={Protocol}, Description={Description})");

        private static readonly Func<ILogger, string, Protocol, string, IDisposable> _colocListenerScope =
            LoggerMessage.DefineScope<string, Protocol, string>(
                "server(Transport={Transport}, Protocol={Protocol}, Server={Server})");

        private static readonly Func<ILogger, long, IDisposable> _colocServerConnectionScope =
            LoggerMessage.DefineScope<long>("connection(ID={ID})");

        private static readonly Func<ILogger, string, Protocol, long, string, IDisposable> _colocClientConnectionScope =
            LoggerMessage.DefineScope<string, Protocol, long, string>(
                "connection(Transport={Transport}, Protocol={Protocol}, ID={ID}, Server={Server})");

        private static readonly Func<ILogger, string, Protocol, string, string, IDisposable> _datagramOverConnectionServerConnectionScope =
            LoggerMessage.DefineScope<string, Protocol, string, string>(
                "server(Transport={Transport}, Protocol={Protocol}, Server={Server}, " +
                "LocalEndPoint={LocalEndPoint})");

        private static readonly Func<ILogger, string, Protocol, string, string, IDisposable> _datagramServerConnectionScope =
            LoggerMessage.DefineScope<string, Protocol, string, string>(
                "server(Transport={Transport}, Protocol={Protocol}, Server={Server}, " +
                "Description={Description})");

        private static readonly Func<ILogger, string, IDisposable> _overConnectionServerConnectionScope =
            LoggerMessage.DefineScope<string>(
                "connection(RemoteEndPoint={RemoteEndpoint})");

        private static readonly Func<ILogger, string, Protocol, string, string, IDisposable> _overConnectionClientConnectionScope =
            LoggerMessage.DefineScope<string, Protocol, string, string>(
                "connection(Transport={Transport}, Protocol={Protocol}, LocalEndPoint={LocalEndpoint}, " +
                "RemoteEndPoint={RemoteEndpoint})");

        private static readonly Func<ILogger, string, IDisposable> _serverConnectionScope =
            LoggerMessage.DefineScope<string>("connection(Description={Description})");

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
        internal static partial void LogConnectionClosed(this ILogger logger, string reason, Exception? exception = null);

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
            Message = "received {Size} bytes")]
        internal static partial void LogReceivedData(this ILogger logger, int size);

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
            Level = LogLevel.Debug,
            Message = "sent {Size} bytes")]
        internal static partial void LogSentData(this ILogger logger, int size);

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

        internal static IDisposable? StartListenerScope(this ILogger logger, Server server, IListener listener)
        {
            if (!logger.IsEnabled(LogLevel.Error))
            {
                return null;
            }

            string transportName = listener.Endpoint.Transport.ToString().ToLowerInvariant();

            // TODO: add back scope for TcpListener
            if (listener is ColocListener)
            {
                return _colocListenerScope(
                    logger,
                    listener.Endpoint.TransportName,
                    listener.Endpoint.Protocol,
                    server.ToString());
            }
            else
            {
                return _listenerScope(
                    logger,
                    listener.Endpoint.TransportName,
                    listener.Endpoint.Protocol,
                    server.ToString(),
                    listener.ToString()!);
            }
        }

        internal static IDisposable? StartConnectionScope(
            this ILogger logger,
            MultiStreamConnection connection,
            Server? server)
        {
            if (!logger.IsEnabled(LogLevel.Error))
            {
                return null;
            }

            try
            {
                if (connection is ColocConnection colocatedConnection)
                {
                    if (connection.IsServer)
                    {
                        return _colocServerConnectionScope(logger, colocatedConnection.Id);
                    }
                    else
                    {
                        // TODO: revisit
                        return _colocClientConnectionScope(
                            logger,
                            connection.TransportName,
                            connection.Protocol,
                            colocatedConnection.Id,
                            connection.LocalEndpoint.ToString());
                    }
                }
                else if (connection.ConnectionInformation is TcpConnectionInformation tcpConnection)
                {
                    if (connection.IsDatagram && server != null)
                    {
                        try
                        {
                            return _datagramOverConnectionServerConnectionScope(
                                logger,
                                connection.TransportName,
                                connection.Protocol,
                                server.ToString(),
                                tcpConnection.LocalEndPoint?.ToString() ?? "undefined");
                        }
                        catch (System.Net.Sockets.SocketException)
                        {
                            return _datagramServerConnectionScope(
                                logger,
                                connection.TransportName,
                                connection.Protocol,
                                server.ToString(),
                                "not connected");
                        }
                    }
                    else
                    {
                        try
                        {
                            if (connection.IsServer)
                            {
                                return _overConnectionServerConnectionScope(
                                    logger,
                                    tcpConnection.RemoteEndPoint?.ToString() ?? "undefined");
                            }
                            else
                            {
                                return _overConnectionClientConnectionScope(
                                    logger,
                                    connection.TransportName,
                                    connection.Protocol,
                                    tcpConnection.LocalEndPoint?.ToString() ?? "undefined",
                                    tcpConnection.RemoteEndPoint?.ToString() ?? "undefined");
                            }
                        }
                        catch (System.Net.Sockets.SocketException)
                        {
                            if (connection.IsServer)
                            {
                                return _serverConnectionScope(logger, "not connected");
                            }
                            else
                            {
                                return _clientConnectionScope(
                                    logger,
                                    connection.TransportName,
                                    connection.Protocol,
                                    "not connected");
                            }
                        }
                    }
                }
                else
                {
                    if (connection.IsDatagram && server != null)
                    {
                        return _datagramServerConnectionScope(
                            logger,
                            connection.TransportName,
                            connection.Protocol,
                            server.ToString(),
                            connection.ToString()!);
                    }
                    else if (connection.IsServer)
                    {
                        return _serverConnectionScope(logger, connection.ToString()!);
                    }
                    else
                    {
                        return _clientConnectionScope(logger, connection.TransportName, connection.Protocol, connection.ToString()!);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                return null;
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
