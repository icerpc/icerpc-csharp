// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Net;

namespace IceRpc.Transports.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging transport messages.</summary>
    internal static class TransportLoggerExtensions
    {
        private static readonly Action<ILogger, Exception> _acceptingConnectionFailed =
            LoggerMessage.Define(
                LogLevel.Error,
                TransportEventIds.AcceptingConnectionFailed,
                "unexpected failure to accept a new connection");

        private static readonly Func<ILogger, string, Protocol, string, string, IDisposable> _acceptorScope =
            LoggerMessage.DefineScope<string, Protocol, string, string>(
                "server(Transport={Transport}, Protocol={Protocol}, Server={Server}, Description={Description})");

        private static readonly Func<ILogger, string, Protocol, string, IDisposable> _clientConnectionScope =
            LoggerMessage.DefineScope<string, Protocol, string>(
                "connection(Transport={Transport}, Protocol={Protocol}, Description={Description})");

        private static readonly Func<ILogger, string, Protocol, string, IDisposable> _colocAcceptorScope =
            LoggerMessage.DefineScope<string, Protocol, string>(
                "server(Transport={Transport}, Protocol={Protocol}, Server={Server})");

        private static readonly Func<ILogger, long, IDisposable> _colocServerConnectionScope =
            LoggerMessage.DefineScope<long>("connection(ID={ID})");

        private static readonly Func<ILogger, string, Protocol, long, string, IDisposable> _colocClientConnectionScope =
            LoggerMessage.DefineScope<string, Protocol, long, string>(
                "connection(Transport={Transport}, Protocol={Protocol}, ID={ID}, Server={Server})");

        private static readonly Action<ILogger, Exception> _connectionAccepted =
            LoggerMessage.Define(
                LogLevel.Debug,
                TransportEventIds.ConnectionAccepted,
                "accepted connection");

        private static readonly Action<ILogger, Exception> _connectionAcceptFailed =
            LoggerMessage.Define(
                LogLevel.Debug,
                TransportEventIds.ConnectionAcceptFailed,
                "failed to accept connection");

        private static readonly Action<ILogger, Exception> _connectionConnectFailed =
            LoggerMessage.Define(
                LogLevel.Debug,
                TransportEventIds.ConnectionConnectFailed,
                "connection establishment failed");

        private static readonly Action<ILogger, Exception> _connectionEstablished =
            LoggerMessage.Define(
                LogLevel.Debug,
                TransportEventIds.ConnectionConnected,
                "established connection");

        private static readonly Action<ILogger, string, Exception> _connectionEventHandlerException =
            LoggerMessage.Define<string>(
                LogLevel.Warning,
                TransportEventIds.ConnectionEventHandlerException,
                "{Name} event handler raised exception");

        private static readonly Action<ILogger, string, Exception> _connectionClosed =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                TransportEventIds.ConnectionConnectFailed,
                "closed connection (Reason={Reason})");

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

        private static readonly Action<ILogger, string, int, int, Exception> _receiveBufferSizeAdjusted =
            LoggerMessage.Define<string, int, int>(
                LogLevel.Debug,
                TransportEventIds.ReceiveBufferSizeAdjusted,
                "{Transport} receive buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}");

        private static readonly Action<ILogger, int, Exception> _receivedData =
            LoggerMessage.Define<int>(
                LogLevel.Trace,
                TransportEventIds.ReceivedData,
                "received {Size} bytes");

        private static readonly Action<ILogger, int, Exception> _receivedInvalidDatagram =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                TransportEventIds.ReceivedInvalidDatagram,
                "received invalid {Bytes} bytes datagram");

        private static readonly Action<ILogger, string, int, int, Exception> _sendBufferSizeAdjusted =
            LoggerMessage.Define<string, int, int>(
                LogLevel.Debug,
                TransportEventIds.SendBufferSizeAdjusted,
                "{Transport} send buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}");

        private static readonly Action<ILogger, int, Exception> _sentData =
            LoggerMessage.Define<int>(
                LogLevel.Trace,
                TransportEventIds.SentData,
                "sent {Size} bytes");

        private static readonly Func<ILogger, string, IDisposable> _serverConnectionScope =
            LoggerMessage.DefineScope<string>("connection(Description={Description})");

        private static readonly Action<ILogger, Exception> _startAcceptingConnections =
            LoggerMessage.Define(
                LogLevel.Information,
                TransportEventIds.StartAcceptingConnections,
                "starting to accept connections");

        private static readonly Action<ILogger, Exception> _startReceivingDatagrams =
            LoggerMessage.Define(
                LogLevel.Information,
                TransportEventIds.StartReceivingDatagrams,
                "starting to receive datagrams");

        private static readonly Action<ILogger, Exception> _startReceivingDatagramsFailed =
            LoggerMessage.Define(
                LogLevel.Information,
                TransportEventIds.StartReceivingDatagramsFailed,
                "starting receiving datagrams failed");

        private static readonly Action<ILogger, Exception> _startSendingDatagrams =
            LoggerMessage.Define(
                LogLevel.Debug,
                TransportEventIds.StartSendingDatagrams,
                "starting to send datagrams");

        private static readonly Action<ILogger, Exception> _startSendingDatagramsFailed =
            LoggerMessage.Define(
                LogLevel.Debug,
                TransportEventIds.StartSendingDatagramsFailed,
                "starting sending datagrams failed");

        private static readonly Action<ILogger, Exception> _stopAcceptingConnections =
            LoggerMessage.Define(
                LogLevel.Information,
                TransportEventIds.StopAcceptingConnections,
                "stopping to accept connections");

        private static readonly Action<ILogger, Exception> _stopReceivingDatagrams =
            LoggerMessage.Define(
                LogLevel.Information,
                TransportEventIds.StopReceivingDatagrams,
                "stopping to receive datagrams");

        private static readonly Func<ILogger, long, string, string, IDisposable> _streamScope =
            LoggerMessage.DefineScope<long, string, string>("stream(ID={ID}, InitiatedBy={InitiatedBy}, Kind={Kind})");

        private static readonly Func<ILogger, string, Protocol, string, EndPoint, IDisposable> _tcpAcceptorScope =
            LoggerMessage.DefineScope<string, Protocol, string, EndPoint>(
                "server(Transport={Transport}, Protocol={Protocol}, Server={Server}, " +
                "LocalEndPoint={LocalEndPoint})");

        internal static void LogAcceptingConnectionFailed(this ILogger logger, Exception ex) =>
            _acceptingConnectionFailed(logger, ex);

        internal static void LogConnectionEventHandlerException(this ILogger logger, string name, Exception ex) =>
            _connectionEventHandlerException(logger, name, ex);

        internal static void LogConnectionAccepted(this ILogger logger) =>
            _connectionAccepted(logger, null!);

        internal static void LogConnectionAcceptFailed(this ILogger logger, Exception exception) =>
            _connectionAcceptFailed(logger, exception);

        internal static void LogConnectionClosed(this ILogger logger, string message, Exception? exception = null) =>
            _connectionClosed(logger, message, exception!);

        internal static void LogConnectionConnectFailed(this ILogger logger, Exception exception) =>
            _connectionConnectFailed(logger, exception);

        internal static void LogConnectionEstablished(this ILogger logger) =>
            _connectionEstablished(logger, null!);

        internal static void LogReceivedInvalidDatagram(this ILogger logger, int bytes) =>
            _receivedInvalidDatagram(logger, bytes, null!);

        internal static void LogStartReceivingDatagrams(this ILogger logger) =>
            _startReceivingDatagrams(logger, null!);

        internal static void LogStartReceivingDatagramsFailed(this ILogger logger, Exception exception) =>
            _startReceivingDatagramsFailed(logger, exception);

        internal static void LogStartSendingDatagrams(this ILogger logger) =>
            _startSendingDatagrams(logger, null!);

        internal static void LogStartSendingDatagramsFailed(this ILogger logger, Exception exception) =>
            _startSendingDatagramsFailed(logger, exception);

        internal static void LogStartAcceptingConnections(this ILogger logger) =>
            _startAcceptingConnections(logger, null!);

        internal static void LogStopAcceptingConnections(this ILogger logger) =>
            _stopAcceptingConnections(logger, null!);

        internal static void LogStopReceivingDatagrams(this ILogger logger) =>
            _stopReceivingDatagrams(logger, null!);

        internal static void LogReceivedData(this ILogger logger, int size) => _receivedData(logger, size, null!);

        internal static void LogReceiveBufferSizeAdjusted(
            this ILogger logger,
            Transport transport,
            int requestedSize,
            int adjustedSize) =>
            _receiveBufferSizeAdjusted(
                logger,
                transport.ToString().ToLowerInvariant(),
                requestedSize,
                adjustedSize,
                null!);

        internal static void LogSendBufferSizeAdjusted(
            this ILogger logger,
            Transport transport,
            int requestedSize,
            int adjustedSize) =>
            _sendBufferSizeAdjusted(
                logger,
                transport.ToString().ToLowerInvariant(),
                requestedSize,
                adjustedSize,
                null!);

        internal static void LogSentData(this ILogger logger, int size) => _sentData(logger, size, null!);

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
                    if (connection.IsIncoming)
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
                else if (connection.ConnectionInformation is ITcpConnectionInformation tcpConnection)
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
                            if (connection.IsIncoming)
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
                            if (connection.IsIncoming)
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
                    else if (connection.IsIncoming)
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

        internal static IDisposable? StartAcceptorScope(this ILogger logger, Server server, IAcceptor acceptor)
        {
            if (!logger.IsEnabled(LogLevel.Error))
            {
                return null;
            }

            string transportName = acceptor.Endpoint.Transport.ToString().ToLowerInvariant();
            if (acceptor is TcpAcceptor tcpAcceptor)
            {
                return _tcpAcceptorScope(
                    logger,
                    acceptor.Endpoint.TransportName,
                    acceptor.Endpoint.Protocol,
                    server.ToString(),
                    tcpAcceptor.IPEndPoint);
            }
            else if (acceptor is ColocAcceptor)
            {
                return _colocAcceptorScope(
                    logger,
                    acceptor.Endpoint.TransportName,
                    acceptor.Endpoint.Protocol,
                    server.ToString());
            }
            else
            {
                return _acceptorScope(
                    logger,
                    acceptor.Endpoint.TransportName,
                    acceptor.Endpoint.Protocol,
                    server.ToString(),
                    acceptor.ToString()!);
            }
        }
    }
}
