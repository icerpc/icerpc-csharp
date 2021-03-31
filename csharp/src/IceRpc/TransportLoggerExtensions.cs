// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Net;

namespace IceRpc
{
    /// <summary>This class contains ILogger extensions methods for logging transport messages.</summary>
    internal static class TransportLoggerExtensions
    {
        private const int BaseEventId = LoggerExtensions.TransportBaseEventId;
        private const int AcceptingConnections = BaseEventId + 0;
        private const int AcceptingConnectionFailed = BaseEventId + 1;
        private const int ConnectionAccepted = BaseEventId + 2;
        private const int ConnectionCallbackException = BaseEventId + 3;
        private const int ConnectionClosed = BaseEventId + 4;
        private const int ConnectionEstablished = BaseEventId + 5;
        private const int ConnectionException = BaseEventId + 6;
        private const int DatagramConnectionReceiveCloseConnectionFrame = BaseEventId + 7;
        private const int DatagramSizeExceededIncomingFrameMaxSize = BaseEventId + 8;
        private const int MaximumDatagramSizeExceeded = BaseEventId + 9;
        private const int PingEventHandlerException = BaseEventId + 10;
        private const int ReceiveBufferSizeAdjusted = BaseEventId + 11;
        private const int ReceivedData = BaseEventId + 12;
        private const int ReceivedInvalidDatagram = BaseEventId + 13;
        private const int SendBufferSizeAdjusted = BaseEventId + 14;
        private const int SentData = BaseEventId + 15;
        private const int StartAcceptingConnections = BaseEventId + 16;
        private const int StartReceivingDatagrams = BaseEventId + 17;
        private const int StartSendingDatagrams = BaseEventId + 18;
        private const int StopAcceptingConnections = BaseEventId + 19;

        private static readonly Action<ILogger, string, Exception> _acceptingConnections =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(AcceptingConnections, nameof(AcceptingConnections)),
                "accepting {Transport} connections");

        private static readonly Action<ILogger, string, Exception> _acceptingConnectionFailed =
            LoggerMessage.Define<string>(
                LogLevel.Error,
                new EventId(AcceptingConnectionFailed, nameof(AcceptingConnectionFailed)),
                "failed to accept {Transport} connection");

        private static readonly Func<ILogger, string, string, string, IDisposable> _acceptorScope =
            LoggerMessage.DefineScope<string, string, string>("server({Transport}, Name={Name}, {Description})");

        private static readonly Func<ILogger, string, string, IDisposable> _colocatedAcceptorScope =
            LoggerMessage.DefineScope<string, string>("server({Transport}, Name={Name})");

        private static readonly Func<ILogger, string, long, IDisposable> _colocatedSocketScope =
            LoggerMessage.DefineScope<string, long>("socket({Transport}, ID={ID})");

        private static readonly Action<ILogger, string, Exception> _connectionAccepted =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(ConnectionAccepted, nameof(ConnectionAccepted)),
                "accepted {Transport} connection");

        private static readonly Action<ILogger, string, Exception> _connectionEstablished =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(ConnectionEstablished, nameof(ConnectionEstablished)),
                "established {Transport} connection");

        private static readonly Action<ILogger, Exception> _connectionCallbackException = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(ConnectionCallbackException, nameof(ConnectionCallbackException)),
            "connection callback exception");

        private static readonly Action<ILogger, Exception> _connectionClosed = LoggerMessage.Define(
            LogLevel.Debug,
            new EventId(ConnectionClosed, nameof(ConnectionCallbackException)),
            "closed connection");

        private static readonly Action<ILogger, Exception> _connectionException = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(ConnectionException, nameof(ConnectionException)),
            "connection exception");

        private static readonly Action<ILogger, Exception> _datagramConnectionReceiveCloseConnectionFrame =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(DatagramConnectionReceiveCloseConnectionFrame,
                            nameof(DatagramConnectionReceiveCloseConnectionFrame)),
                "ignoring close connection frame for datagram connection");

        private static readonly Func<ILogger, string, string, string, IDisposable> _datagramOverSocketServerSocketScope =
            LoggerMessage.DefineScope<string, string, string>("server({Transport}, Name={Name}, Address={Address})");

        private static readonly Func<ILogger, string, string, string, IDisposable> _datagramServerSocketScope =
            LoggerMessage.DefineScope<string, string, string>("server({Transport}, Name={Name}, {Description})");

        private static readonly Action<ILogger, int, Exception> _maximumDatagramSizeExceeded =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                new EventId(MaximumDatagramSizeExceeded, nameof(MaximumDatagramSizeExceeded)),
                "maximum datagram size of {Size} exceeded");

        private static readonly Func<ILogger, string, string, string, IDisposable> _overSocketSocketScope =
            LoggerMessage.DefineScope<string, string, string>(
                "socket({Transport}, LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint})");

        private static readonly Action<ILogger, Exception> _pingEventHanderException = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(PingEventHandlerException, nameof(PingEventHandlerException)),
            "ping event handler raised an exception");

        private static readonly Action<ILogger, string, int, int, Exception> _receiveBufferSizeAdjusted =
            LoggerMessage.Define<string, int, int>(
                LogLevel.Debug,
                new EventId(ReceiveBufferSizeAdjusted, nameof(ReceiveBufferSizeAdjusted)),
                "{Transport} receive buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}");

        private static readonly Action<ILogger, int, string, Exception> _receivedData =
            LoggerMessage.Define<int, string>(
                LogLevel.Debug,
                new EventId(ReceivedData, nameof(ReceivedData)),
                "received {Size} bytes via {Transport}");
        private static readonly Action<ILogger, int, Exception> _receivedDatagramExceededIncomingFrameMaxSize =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                new EventId(DatagramSizeExceededIncomingFrameMaxSize, nameof(DatagramSizeExceededIncomingFrameMaxSize)),
                "frame with {Size} bytes exceeds IncomingFrameMaxSize connection option value");

        private static readonly Action<ILogger, int, Exception> _receivedInvalidDatagram =
            LoggerMessage.Define<int>(
                LogLevel.Error,
                new EventId(ReceivedInvalidDatagram, nameof(ReceivedInvalidDatagram)),
                "received datagram with {Bytes} bytes");

        private static readonly Action<ILogger, string, int, int, Exception> _sendBufferSizeAdjusted =
            LoggerMessage.Define<string, int, int>(
                LogLevel.Debug,
                new EventId(SendBufferSizeAdjusted, nameof(SendBufferSizeAdjusted)),
                "{Transport} send buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}");

        private static readonly Action<ILogger, int, string, Exception> _sentData =
            LoggerMessage.Define<int, string>(
                LogLevel.Debug,
                new EventId(SentData, nameof(SentData)),
                "sent {Size} bytes via {Transport}");

        private static readonly Func<ILogger, string, string, IDisposable> _socketScope =
            LoggerMessage.DefineScope<string, string>("socket({Transport} {Description})");

        private static readonly Action<ILogger, string, Exception> _startAcceptingConnections =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(StartAcceptingConnections, nameof(StartAcceptingConnections)),
                "starting to accept {Transport} connections");

        private static readonly Action<ILogger, string, Exception> _startReceivingDatagrams =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(StartReceivingDatagrams, nameof(StartReceivingDatagrams)),
                "starting to receive {Transport} datagrams");

        private static readonly Action<ILogger, string, Exception> _startSendingDatagrams =
            LoggerMessage.Define<string>(
                LogLevel.Debug,
                new EventId(StartSendingDatagrams, nameof(StartSendingDatagrams)),
                "starting to send {Transport} datagrams");

        private static readonly Action<ILogger, string, Exception> _stopAcceptingConnections =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(StopAcceptingConnections, nameof(StopAcceptingConnections)),
                "stoping to accept {Transport} connections");

        private static readonly Action<ILogger, string, Exception> _stopSendingDatagrams =
            LoggerMessage.Define<string>(
                LogLevel.Information,
                new EventId(StopAcceptingConnections, nameof(StopAcceptingConnections)),
                "stoping to receive {Transport} datagrams");

        private static readonly Func<ILogger, long, string, IDisposable> _streamScope =
            LoggerMessage.DefineScope<long, string>("stream(ID={ID}, {Kind})");

        private static readonly Func<ILogger, string, string, EndPoint, IDisposable> _tcpAcceptorScope =
            LoggerMessage.DefineScope<string, string, EndPoint>("server({Transport}, Name={Name}, Address={Address})");

        internal static void LogAcceptingConnections(this ILogger logger, Transport transport) =>
            _acceptingConnections(logger, transport.ToString().ToLowerInvariant(), null!);

        internal static void LogAcceptingConnectionFailed(this ILogger logger, Transport transport, Exception ex) =>
            _acceptingConnectionFailed(logger, transport.ToString().ToLowerInvariant(), ex);

        internal static void LogConnectionCallbackException(this ILogger logger, Exception ex) =>
            _connectionCallbackException(logger, ex);

        internal static void LogConnectionAccepted(this ILogger logger, Transport transport) =>
            _connectionAccepted(logger, transport.ToString().ToLowerInvariant(), null!);

        internal static void LogConnectionClosed(this ILogger logger, Exception? exception = null) =>
            _connectionClosed(logger, exception!);

        internal static void LogConnectionEstablished(this ILogger logger, Transport transport) =>
            _connectionEstablished(logger, transport.ToString().ToLowerInvariant(), null!);

        internal static void LogConnectionException(this ILogger logger, Exception ex) =>
            _connectionException(logger, ex);

        internal static void LogMaximumDatagramSizeExceeded(this ILogger logger, int bytes) =>
            _maximumDatagramSizeExceeded(logger, bytes, null!);

        internal static void LogReceivedInvalidDatagram(this ILogger logger, int bytes) =>
            _receivedInvalidDatagram(logger, bytes, null!);

        internal static void LogStartReceivingDatagrams(this ILogger logger, Transport transport) =>
            _startReceivingDatagrams(logger, transport.ToString().ToLowerInvariant(), null!);

        internal static void LogStartSendingDatagrams(this ILogger logger, Transport transport) =>
            _startSendingDatagrams(logger, transport.ToString().ToLowerInvariant(), null!);

        internal static void LogStartAcceptingConnections(this ILogger logger, Transport transport) =>
            _startAcceptingConnections(logger, transport.ToString().ToLowerInvariant(), null!);

        internal static void LogStopAcceptingConnections(this ILogger logger, Transport transport) =>
            _stopAcceptingConnections(logger, transport.ToString().ToLowerInvariant(), null!);

        internal static void LogStopSendingDatagrams(this ILogger logger, Transport transport) =>
            _stopSendingDatagrams(logger, transport.ToString().ToLowerInvariant(), null!);

        internal static void LogStopReceivingDatagrams(this ILogger logger, Transport transport) =>
            _startReceivingDatagrams(logger, transport.ToString().ToLowerInvariant(), null!);

        internal static void LogPingEventHandlerException(this ILogger logger, Exception exception) =>
            _pingEventHanderException(logger, exception);

        internal static void LogReceivedData(this ILogger logger, int size, Transport transport) =>
            _receivedData(logger, size, transport.ToString().ToLowerInvariant(), null!);

        internal static void LogSentData(this ILogger logger, int size, Transport transport) =>
            _sentData(logger, size, transport.ToString().ToLowerInvariant(), null!);

        internal static void LogDatagramSizeExceededIncomingFrameMaxSize(this ILogger logger, int size) =>
            _receivedDatagramExceededIncomingFrameMaxSize(logger, size, null!);

        internal static void LogDatagramConnectionReceiveCloseConnectionFrame(this ILogger logger) =>
            _datagramConnectionReceiveCloseConnectionFrame(logger, null!);

        internal static void LogReceiveBufferSizeAdjusted(
            this ILogger logger,
            Transport transport,
            int requestedSize,
            int adjustedSize) =>
            _receiveBufferSizeAdjusted(logger,
                                       transport.ToString().ToLowerInvariant(),
                                       requestedSize,
                                       adjustedSize,
                                       null!);

        internal static void LogSendBufferSizeAdjusted(
            this ILogger logger,
            Transport transport,
            int requestedSize,
            int adjustedSize) =>
            _sendBufferSizeAdjusted(logger,
                                    transport.ToString().ToLowerInvariant(),
                                    requestedSize,
                                    adjustedSize,
                                    null!);

        internal static IDisposable StartSocketScope(
            this ILogger logger,
            Transport transport,
            MultiStreamSocket socket,
            Server? server)
        {
            string transportName = transport.ToString().ToLowerInvariant();
            try
            {
                if (socket is ColocatedSocket colocatedSocket)
                {
                    return _colocatedSocketScope(logger, transportName, colocatedSocket.Id);
                }
                else if(socket is MultiStreamOverSingleStreamSocket overSingleStreamSocket &&
                        overSingleStreamSocket.Underlying.Socket is System.Net.Sockets.Socket dotnetsocket)
                {
                    if (socket.Endpoint.IsDatagram && server != null)
                    {
                        try
                        {
                            return _datagramOverSocketServerSocketScope(
                                logger,
                                transportName,
                                server.Name,
                                dotnetsocket.LocalEndPoint?.ToString() ?? "undefined");
                        }
                        catch (System.Net.Sockets.SocketException)
                        {
                            return _datagramServerSocketScope(logger, transportName, server.Name, "not connected");
                        }
                    }
                    else
                    {
                        try
                        {
                            return _overSocketSocketScope(
                                logger,
                                transportName,
                                dotnetsocket.LocalEndPoint?.ToString() ?? "undefined",
                                dotnetsocket.RemoteEndPoint?.ToString() ?? "undefined");
                        }
                        catch (System.Net.Sockets.SocketException)
                        {
                            return _socketScope(logger, transportName, "not connected");
                        }
                    }
                }
                else
                {
                    if (socket.Endpoint.IsDatagram && server != null)
                    {
                        return _datagramServerSocketScope(logger, transportName, server.Name, socket.ToString()!);
                    }
                    else
                    {
                        return _socketScope(logger, transportName, socket.ToString()!);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                return _socketScope(logger, transportName, "closed");
            }
        }

        internal static IDisposable? StartStreamScope(this ILogger logger, SocketStream stream)
        {
            string streamType = (stream.Id % 4) switch
            {
                0 => "[client-initiated, bidirectional]",
                1 => "[server-initiated, bidirectional]",
                2 => "[client-initiated, unidirectional]",
                _ => "[server-initiated, unidirectional]",
            };
            return _streamScope(logger, stream.Id, streamType);
        }

        internal static IDisposable? StartAcceptorScope(this ILogger logger, Server server, IAcceptor acceptor)
        {
            string transportName = acceptor.Endpoint.Transport.ToString().ToLowerInvariant();
            if (acceptor is TcpAcceptor tcpAcceptor)
            {
                return _tcpAcceptorScope(logger, transportName, server.Name, tcpAcceptor.Address);
            }
            else if (acceptor is ColocatedAcceptor)
            {
                return _colocatedAcceptorScope(logger, transportName, server.Name);
            }
            else
            {
                return _acceptorScope(logger, transportName, server.Name, acceptor.ToString()!);
            }
        }
    }
}
