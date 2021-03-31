// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;

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

        private static readonly Action<ILogger, string, IAcceptor, Exception> _acceptingConnections =
            LoggerMessage.Define<string, IAcceptor>(
                LogLevel.Debug,
                new EventId(AcceptingConnections, nameof(AcceptingConnections)),
                "accepting {Transport} connections at {Acceptor}");

        private static readonly Action<ILogger, string, IAcceptor, Exception> _acceptingConnectionFailed =
            LoggerMessage.Define<string, IAcceptor>(
                LogLevel.Error,
                new EventId(AcceptingConnectionFailed, nameof(AcceptingConnectionFailed)),
                "failed to accept {Transport} connection at {Acceptor}");

        private static readonly Action<ILogger, string, MultiStreamSocket, Exception> _connectionAccepted =
            LoggerMessage.Define<string, MultiStreamSocket>(
                LogLevel.Debug,
                new EventId(ConnectionAccepted, nameof(ConnectionAccepted)),
                "accepted {Transport} connection: {Socket}");

        private static readonly Action<ILogger, string, MultiStreamSocket, Exception> _connectionEstablished =
            LoggerMessage.Define<string, MultiStreamSocket>(
                LogLevel.Debug,
                new EventId(ConnectionEstablished, nameof(ConnectionEstablished)),
                "established {Transport} connection: {Socket}");

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

        private static readonly Action<ILogger, int, Exception> _receivedInvalidDatagram =
            LoggerMessage.Define<int>(
                LogLevel.Error,
                new EventId(ReceivedInvalidDatagram, nameof(ReceivedInvalidDatagram)),
                "received datagram with {Bytes} bytes");

        private static readonly Action<ILogger, string, IAcceptor, Exception> _startAcceptingConnections =
            LoggerMessage.Define<string, IAcceptor>(
                LogLevel.Information,
                new EventId(StartAcceptingConnections, nameof(StartAcceptingConnections)),
                "start accepting {Transport} connections at {Acceptor}");

        private static readonly Action<ILogger, string, IAcceptor, Exception> _stopAcceptingConnections =
            LoggerMessage.Define<string, IAcceptor>(
                LogLevel.Information,
                new EventId(StopAcceptingConnections, nameof(StopAcceptingConnections)),
                "stop accepting {Transport} connections at {Acceptor}");

        private static readonly Action<ILogger, Exception> _pingEventHanderException = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(PingEventHandlerException, nameof(PingEventHandlerException)),
            "ping event handler raised an exception");

        private static readonly Action<ILogger, int, string, Exception> _receivedData =
            LoggerMessage.Define<int, string>(
                LogLevel.Debug,
                new EventId(ReceivedData, nameof(ReceivedData)),
                "received {Size} bytes via {Transport}");

        private static readonly Action<ILogger, int, string, Exception> _sentData =
            LoggerMessage.Define<int, string>(
                LogLevel.Debug,
                new EventId(SentData, nameof(SentData)),
                "sent {Size} bytes via {Transport}");

        private static readonly Action<ILogger, string, MultiStreamSocket, Exception> _startReceivingDatagrams =
            LoggerMessage.Define<string, MultiStreamSocket>(
                LogLevel.Debug,
                new EventId(StartReceivingDatagrams, nameof(StartReceivingDatagrams)),
                "starting to receive {Transport} datagrams: {Socket}");

        private static readonly Action<ILogger, string, MultiStreamSocket, Exception> _startSendingDatagrams =
            LoggerMessage.Define<string, MultiStreamSocket>(
                LogLevel.Debug,
                new EventId(StartSendingDatagrams, nameof(StartSendingDatagrams)),
                "starting to send {Transport} datagrams: {Socket}");

        private static readonly Action<ILogger, int, Exception> _receivedDatagramExceededIncomingFrameMaxSize =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                new EventId(DatagramSizeExceededIncomingFrameMaxSize, nameof(DatagramSizeExceededIncomingFrameMaxSize)),
                "frame with {Size} bytes exceeds IncomingFrameMaxSize connection option value");

        private static readonly Action<ILogger, int, Exception> _maximumDatagramSizeExceeded =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                new EventId(MaximumDatagramSizeExceeded, nameof(MaximumDatagramSizeExceeded)),
                "maximum datagram size of {Size} exceeded");

        private static readonly Action<ILogger, Exception> _datagramConnectionReceiveCloseConnectionFrame =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(DatagramConnectionReceiveCloseConnectionFrame,
                            nameof(DatagramConnectionReceiveCloseConnectionFrame)),
                "ignoring close connection frame for datagram connection");

        private static readonly Action<ILogger, string, int, int, Exception> _receiveBufferSizeAdjusted =
            LoggerMessage.Define<string, int, int>(
                LogLevel.Debug,
                new EventId(ReceiveBufferSizeAdjusted, nameof(ReceiveBufferSizeAdjusted)),
                "{Transport} receive buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}");

        private static readonly Action<ILogger, string, int, int, Exception> _sendBufferSizeAdjusted =
            LoggerMessage.Define<string, int, int>(
                LogLevel.Debug,
                new EventId(SendBufferSizeAdjusted, nameof(SendBufferSizeAdjusted)),
                "{Transport} send buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}");

        private static readonly Func<ILogger, string, MultiStreamSocket, IDisposable> _socketScope =
            LoggerMessage.DefineScope<string, MultiStreamSocket>("socket({Transport}, {Socket})");

        private static readonly Func<ILogger, SocketStream, IDisposable> _streamScope =
            LoggerMessage.DefineScope<SocketStream>("stream({Stream})");

        internal static void LogAcceptingConnections(
            this ILogger logger,
            Transport transport,
            IAcceptor acceptor) =>
            _acceptingConnections(logger, transport.ToString().ToLowerInvariant(), acceptor, null!);

        internal static void LogAcceptingConnectionFailed(
            this ILogger logger,
            Transport transport,
            IAcceptor acceptor,
            Exception ex) =>
            _acceptingConnectionFailed(logger, transport.ToString().ToLowerInvariant(), acceptor, ex);

        internal static void LogConnectionCallbackException(this ILogger logger, Exception ex) =>
            _connectionCallbackException(logger, ex);

        internal static void LogConnectionAccepted(
            this ILogger logger,
            Transport transport,
            MultiStreamSocket socket) =>
            _connectionAccepted(logger, transport.ToString().ToLowerInvariant(), socket, null!);

        internal static void LogConnectionClosed(this ILogger logger, Exception? exception = null) =>
            _connectionClosed(logger, exception!);

        internal static void LogConnectionEstablished(
            this ILogger logger,
            Transport transport,
            MultiStreamSocket socket) =>
            _connectionEstablished(logger, transport.ToString().ToLowerInvariant(), socket, null!);

        internal static void LogConnectionException(this ILogger logger, Exception ex) =>
            _connectionException(logger, ex);

        internal static void LogMaximumDatagramSizeExceeded(this ILogger logger, int bytes) =>
            _maximumDatagramSizeExceeded(logger, bytes, null!);

        internal static void LogReceivedInvalidDatagram(this ILogger logger, int bytes) =>
            _receivedInvalidDatagram(logger, bytes, null!);

        internal static void LogStartReceivingDatagrams(
            this ILogger logger,
            Transport transport,
            MultiStreamSocket socket) =>
            _startReceivingDatagrams(logger, transport.ToString().ToLowerInvariant(), socket, null!);

        internal static void LogStartSendingDatagrams(
            this ILogger logger,
            Transport transport,
            MultiStreamSocket socket) =>
            _startSendingDatagrams(logger, transport.ToString().ToLowerInvariant(), socket, null!);

        internal static void LogStartAcceptingConnections(
            this ILogger logger,
            Transport transport,
            IAcceptor acceptor) =>
            _startAcceptingConnections(logger, transport.ToString().ToLowerInvariant(), acceptor, null!);

        internal static void LogStopAcceptingConnections(
            this ILogger logger,
            Transport transport,
            IAcceptor acceptor) =>
            _stopAcceptingConnections(logger, transport.ToString().ToLowerInvariant(), acceptor, null!);

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
            MultiStreamSocket socket) =>
            _socketScope(logger, transport.ToString().ToLowerInvariant(), socket);

        internal static IDisposable? StartStreamScope(this ILogger logger, SocketStream stream) =>
            _streamScope(logger, stream);
    }
}
