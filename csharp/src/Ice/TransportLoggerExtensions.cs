// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace ZeroC.Ice
{
    internal static class TransportLoggerExtensions
    {
        private const int AcceptingConnection = 0;
        private const int AcceptingConnectionFailed = 1;
        private const int BindingSocketAttempt = 2;
        private const int ConnectionAccepted = 3;
        private const int ConnectionCallbackException = 4;
        private const int ConnectionClosed = 5;
        private const int ConnectionEstablished = 6;
        private const int ConnectionException = 7;
        private const int DatagramConnectionReceiveCloseConnectionFrame = 8;
        private const int DatagramSizeExceededIncomingFrameMaxSize = 9;
        private const int HttpUpgradeRequestAccepted = 10;
        private const int HttpUpgradeRequestFailed = 11;
        private const int HttpUpgradeRequestSucceed = 12;
        private const int MaximumDatagramSizeExceeded = 13;
        private const int ObjectAdapterPublishedEndpoints = 14;
        private const int PingEventHandlerException = 15;
        private const int ReceiveBufferSizeAdjusted = 16;
        private const int ReceivedData = 17;
        private const int ReceivedInvalidDatagram = 18;
        private const int ReceivedSlicInitializeAckFrame = 19;
        private const int ReceivedSlicInitializeFrame = 20;
        private const int ReceivedSlicPingFrame = 21;
        private const int ReceivedSlicPongFrame = 22;
        private const int ReceivedSlicStreamConsumedFrame = 23;
        private const int ReceivedSlicStreamFrame = 24;
        private const int ReceivedSlicStreamLastFrame = 25;
        private const int ReceivedSlicStreamResetFrame = 26;
        private const int ReceivedSlicVersionFrame = 27;
        private const int ReceivedWebSocketFrame = 28;
        private const int SendBufferSizeAdjusted = 29;
        private const int SendingSlicInitializeAckFrame = 30;
        private const int SendingSlicInitializeFrame = 31;
        private const int SendingSlicPingFrame = 32;
        private const int SendingSlicPongFrame = 33;
        private const int SendingSlicStreamConsumedFrame = 34;
        private const int SendingSlicStreamFrame = 35;
        private const int SendingSlicStreamLastFrame = 36;
        private const int SendingSlicStreamResetFrame = 37;
        private const int SendingSlicVersionFrame = 38;
        private const int SendingWebSocketFrame = 39;
        private const int SentData = 40;
        private const int StartAcceptingConnections = 41;
        private const int StartReceivingDatagrams = 42;
        private const int StartSendingDatagrams = 43;
        private const int StopAcceptingConnections = 44;

        private static readonly Action<ILogger, Transport, string, Exception> _acceptingConnection =
            LoggerMessage.Define<Transport, string>(
                LogLevel.Debug,
                new EventId(AcceptingConnection, nameof(AcceptingConnection)),
                "accepting {Transport} connection at {LocalAddress}");

        private static readonly Action<ILogger, Transport, string, Exception> _acceptingConnectionFailed =
            LoggerMessage.Define<Transport, string>(
                LogLevel.Error,
                new EventId(AcceptingConnectionFailed, nameof(AcceptingConnectionFailed)),
                "failed to accept {Transport} connection at {LocalAddress}");

        private static readonly Action<ILogger, Transport, string, Exception> _bindingSocketAttempt =
            LoggerMessage.Define<Transport, string>(
                LogLevel.Debug,
                new EventId(BindingSocketAttempt, nameof(BindingSocketAttempt)),
                "attempting to bind to {Transport} socket: local address = {LocalAddress}");

        private static readonly Action<ILogger, Transport, string, string, Exception> _connectionAccepted =
            LoggerMessage.Define<Transport, string, string>(
                LogLevel.Debug,
                new EventId(ConnectionAccepted, nameof(ConnectionAccepted)),
                "accepted {Transport} connection: local address = {LocalAddress}, peer address = {PeerAddress}");

        private static readonly Action<ILogger, Transport, string, string, Exception> _connectionEstablished =
            LoggerMessage.Define<Transport, string, string>(
                LogLevel.Debug,
                new EventId(ConnectionEstablished, nameof(ConnectionEstablished)),
                "established {Transport} connection: local address {LocalAddress}, peer address {PeerAddress}");

        private static readonly Action<ILogger, Exception> _connectionCallbackException = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(ConnectionCallbackException, nameof(ConnectionCallbackException)),
            "connection callback exception");

        private static readonly Action<ILogger, Transport, Exception> _connectionClosed =
            LoggerMessage.Define<Transport>(
                LogLevel.Debug,
                new EventId(ConnectionClosed, nameof(ConnectionCallbackException)),
                "closed {Transport} connection");

        private static readonly Action<ILogger, Exception> _connectionException = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(ConnectionException, nameof(ConnectionException)),
            "connection exception");

        private static readonly Action<ILogger, Transport, Exception> _httpUpgradeRequestAccepted =
            LoggerMessage.Define<Transport>(
                LogLevel.Error,
                new EventId(HttpUpgradeRequestAccepted, nameof(HttpUpgradeRequestAccepted)),
                "accepted {Transport} connection HTTP upgrade request");

        private static readonly Action<ILogger, Transport, Exception> _httpUpgradeRequestFailed =
            LoggerMessage.Define<Transport>(
                LogLevel.Error,
                new EventId(HttpUpgradeRequestFailed, nameof(HttpUpgradeRequestFailed)),
                "{Transport} connection HTTP upgrade request failed");

        private static readonly Action<ILogger, Transport, Exception> _httpUpgradeRequestSucceed =
            LoggerMessage.Define<Transport>(
                LogLevel.Debug,
                new EventId(HttpUpgradeRequestSucceed, nameof(HttpUpgradeRequestSucceed)),
                "{Transport} connection HTTP upgrade request succeed");

        private static readonly Action<ILogger, int, Exception> _receivedInvalidDatagram =
            LoggerMessage.Define<int>(
                LogLevel.Error,
                new EventId(ReceivedInvalidDatagram, nameof(ReceivedInvalidDatagram)),
                "received datagram with {Bytes} bytes");

        private static readonly Action<ILogger, string, IReadOnlyList<Endpoint>, Exception> _objectAdapterPublishedEndpoints =
            LoggerMessage.Define<string, IReadOnlyList<Endpoint>>(
                LogLevel.Information,
                new EventId(ObjectAdapterPublishedEndpoints, nameof(ObjectAdapterPublishedEndpoints)),
                "published endpoints for object adapter {Name}: {Endpoints}");

        private static readonly Action<ILogger, Transport, WSSocket.OpCode, int, Exception> _receivedWebSocketFrame =
            LoggerMessage.Define<Transport, WSSocket.OpCode, int>(
                LogLevel.Debug,
                new EventId(ReceivedWebSocketFrame, nameof(ReceivedWebSocketFrame)),
                "received {Transport} {OpCode} frame with {Size} bytes payload");

        private static readonly Action<ILogger, Transport, WSSocket.OpCode, int, Exception> _sendingWebSocketFrame =
            LoggerMessage.Define<Transport, WSSocket.OpCode, int>(
                LogLevel.Debug,
                new EventId(SendingWebSocketFrame, nameof(SendingWebSocketFrame)),
                "sending {Transport} {OpCode} frame with {Size} bytes payload");

        private static readonly Action<ILogger, Transport, IAcceptor, Exception> _startAcceptingConnections =
            LoggerMessage.Define<Transport, IAcceptor>(
                LogLevel.Information,
                new EventId(StartAcceptingConnections, nameof(StartAcceptingConnections)),
                "start accepting {Transport} connections at {Acceptor}");

        private static readonly Action<ILogger, Transport, IAcceptor, Exception> _stopAcceptingConnections =
            LoggerMessage.Define<Transport, IAcceptor>(
                LogLevel.Information,
                new EventId(StopAcceptingConnections, nameof(StopAcceptingConnections)),
                "stop accepting {Transport} connections at {Acceptor}");

        private static readonly Action<ILogger, Exception> _pingEventHanderException = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(PingEventHandlerException, nameof(PingEventHandlerException)),
            "ping event handler raised an exception");

        private static readonly Action<ILogger, int, Transport, Exception> _receivedData =
            LoggerMessage.Define<int, Transport>(
                LogLevel.Debug,
                new EventId(ReceivedData, nameof(ReceivedData)),
                "received {Size} bytes via {Transport}");

        private static readonly Action<ILogger, int, Transport, Exception> _sentData =
            LoggerMessage.Define<int, Transport>(
                LogLevel.Debug,
                new EventId(SentData, nameof(SentData)),
                "sent {Size} bytes via {Transport}");

        private static readonly Action<ILogger, Transport, string, string, IReadOnlyList<string>, Exception> _startReceivingDatagrams =
            LoggerMessage.Define<Transport, string, string, IReadOnlyList<string>>(
                LogLevel.Debug,
                new EventId(StartReceivingDatagrams, nameof(StartReceivingDatagrams)),
                "starting to receive {Transport} datagrams: local address = {LocalAddress}, " +
                "peer address = {PeerAddress}, local interfaces = {LocalInterfaces}");

        private static readonly Action<ILogger, Transport, string, string, IReadOnlyList<string>, Exception> _startSendingDatagrams =
            LoggerMessage.Define<Transport, string, string, IReadOnlyList<string>>(
                LogLevel.Debug,
                new EventId(StartSendingDatagrams, nameof(StartSendingDatagrams)),
                "starting to send {Transport} datagrams: local address = {LocalAddress}, " +
                "peer address = {PeerAddress}, local interfaces = {LocalInterfaces}");

        private static readonly Action<ILogger, int, Exception> _receivedDatagramExceededIncomingFrameMaxSize =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                new EventId(DatagramSizeExceededIncomingFrameMaxSize, nameof(DatagramSizeExceededIncomingFrameMaxSize)),
                "frame with {Size} bytes exceeds Ice.IncomingFrameMaxSize value");

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

        private static readonly Action<ILogger, Transport, int, int, Exception> _receiveBufferSizeAdjusted =
            LoggerMessage.Define<Transport, int, int>(
                LogLevel.Debug,
                new EventId(ReceiveBufferSizeAdjusted, nameof(ReceiveBufferSizeAdjusted)),
                "{Transport} receive buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}");

        private static readonly Action<ILogger, Transport, int, int, Exception> _sendBufferSizeAdjusted =
            LoggerMessage.Define<Transport, int, int>(
                LogLevel.Debug,
                new EventId(SendBufferSizeAdjusted, nameof(SendBufferSizeAdjusted)),
                "{Transport} send buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}");

        private static readonly Action<ILogger, int, Exception> _receivedInitializeFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(ReceivedSlicInitializeFrame, nameof(ReceivedSlicInitializeFrame)),
            "received Slic initialize frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _receivedInitializeAckFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(ReceivedSlicInitializeAckFrame, nameof(ReceivedSlicInitializeAckFrame)),
            "received Slic initialize ack frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _receivedVersionFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(ReceivedSlicVersionFrame, nameof(ReceivedSlicVersionFrame)),
            "received Slic version frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _receivedPingFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(ReceivedSlicPingFrame, nameof(ReceivedSlicPingFrame)),
            "received Slic ping frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _receivedPongFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(ReceivedSlicPongFrame, nameof(ReceivedSlicPongFrame)),
            "received Slic pong frame: size = {Size}");

        private static readonly Action<ILogger, int, long, Exception> _receivedStreamFrame =
            LoggerMessage.Define<int, long>(
                LogLevel.Debug,
                new EventId(ReceivedSlicStreamFrame, nameof(ReceivedSlicStreamFrame)),
                "received Slic stream frame: size = {Size}, streamId = {StreamId}");

        private static readonly Action<ILogger, int, long, Exception> _receivedStreamLastFrame =
            LoggerMessage.Define<int, long>(
                LogLevel.Debug,
                new EventId(ReceivedSlicStreamLastFrame, nameof(ReceivedSlicStreamLastFrame)),
                "received Slic stream last frame: size = {Size}, streamId = {StreamId}");

        private static readonly Action<ILogger, int, Exception> _receivedStreamResetFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(ReceivedSlicStreamResetFrame, nameof(ReceivedSlicStreamResetFrame)),
            "received Slic stream reset frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _receivedStreamConsumedFrame =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                new EventId(ReceivedSlicStreamConsumedFrame),
                "received Slic stream consumed frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _sendingInitializeFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(SendingSlicInitializeFrame, nameof(SendingSlicInitializeFrame)),
            "sending Slic initialize frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _sendingInitializeAckFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(SendingSlicInitializeAckFrame, nameof(SendingSlicInitializeAckFrame)),
            "sending Slic initialize ack frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _sendingVersionFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(SendingSlicVersionFrame, nameof(SendingSlicInitializeAckFrame)),
            "sending Slic version frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _sendingPingFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(SendingSlicPingFrame, nameof(SendingSlicPingFrame)),
            "sending Slic ping frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _sendingPongFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(SendingSlicPongFrame, nameof(SendingSlicPongFrame)),
            "sending Slic pong frame: size = {Size}");

        private static readonly Action<ILogger, int, long, Exception> _sendingStreamFrame =
            LoggerMessage.Define<int, long>(
                LogLevel.Debug,
                new EventId(SendingSlicStreamFrame, nameof(SendingSlicStreamFrame)),
                "sending Slic stream frame: size = {Size}, streamId = {StreamId}");

        private static readonly Action<ILogger, int, long, Exception> _sendingStreamLastFrame =
            LoggerMessage.Define<int, long>(
                LogLevel.Debug,
                new EventId(SendingSlicStreamLastFrame, nameof(SendingSlicStreamLastFrame)),
                "sending Slic stream last frame: size = {Size}, streamId = {StreamId}");

        private static readonly Action<ILogger, int, Exception> _sendingStreamResetFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(SendingSlicStreamResetFrame, nameof(SendingSlicStreamResetFrame)),
            "sending Slic stream reset frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _sendingStreamConsumedFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            new EventId(SendingSlicStreamConsumedFrame, nameof(SendingSlicStreamConsumedFrame)),
            "sending Slic stream consumed frame: size = {Size}");

        private static readonly Func<ILogger, Transport, string, string, IDisposable> _socketScope =
            LoggerMessage.DefineScope<Transport, string, string>(
                "socket({Transport}, local address = {LocalAddress}, peer address =  {PeerAddress})");

        private static readonly Func<ILogger, Transport, string, string, IReadOnlyList<string>, IDisposable> _datagramSocketScope =
            LoggerMessage.DefineScope<Transport, string, string, IReadOnlyList<string>>(
                "socket({Transport}, local address = {LocalAddress}, peer address =  {PeerAddress}, " +
                "interfaces = {Interfaces})");

        private static readonly Func<ILogger, Transport, string, string, IReadOnlyList<string>, IDisposable> _multicastSocketScope =
            LoggerMessage.DefineScope<Transport, string, string, IReadOnlyList<string>>(
                "socket({Transport}, local address = {LocalAddress}, multicast address =  {PeerAddress}, " +
                "interfaces = {Interfaces})");

        internal static void LogAcceptingConnection(
            this ILogger logger,
            Transport transport,
            string localAddress) =>
            _acceptingConnection(logger, transport, localAddress, null!);

        internal static void LogAcceptingConnectionFailed(
            this ILogger logger,
            Transport transport,
            string localAddress,
            Exception ex) =>
            _acceptingConnectionFailed(logger, transport, localAddress, ex);

        internal static void LogConnectionCallbackException(this ILogger logger, Exception ex) =>
            _connectionCallbackException(logger, ex);

        internal static void LogConnectionAccepted(
            this ILogger logger,
            Transport transport,
            string localAddress,
            string remoteAddress) =>
            _connectionAccepted(logger, transport, localAddress, remoteAddress, null!);

        internal static void LogConnectionClosed(
            this ILogger logger,
            Transport transport,
            Exception? exception = null) =>
            _connectionClosed(logger, transport, exception!);

        internal static void LogConnectionEstablished(
            this ILogger logger,
            Transport transport,
            string localAddress,
            string peerAddress) =>
            _connectionEstablished(logger, transport, localAddress, peerAddress, null!);

        internal static void LogConnectionException(this ILogger logger, Exception ex) =>
            _connectionException(logger, ex);

        internal static void LogHttpUpgradeRequestAccepted(
            this ILogger logger,
            Transport transport) =>
            _httpUpgradeRequestAccepted(logger, transport, null!);

        internal static void LogHttpUpgradeRequestFailed(
            this ILogger logger,
            Transport transport,
            Exception ex) =>
            _httpUpgradeRequestFailed(logger, transport, ex);

        internal static void LogHttpUpgradeRequestSucceed(
            this ILogger logger,
            Transport transport) =>
            _httpUpgradeRequestSucceed(logger, transport, null!);

        internal static void LogMaximumDatagramSizeExceeded(this ILogger logger, int bytes) =>
            _maximumDatagramSizeExceeded(logger, bytes, null!);

        internal static void LogObjectAdapterPublishedEndpoints(
            this ILogger logger,
            string name,
            IReadOnlyList<Endpoint> endpoints) =>
            _objectAdapterPublishedEndpoints(logger, name, endpoints, null!);

        internal static void LogReceivedInvalidDatagram(this ILogger logger, int bytes) =>
            _receivedInvalidDatagram(logger, bytes, null!);

        internal static void LogReceivedWebSocketFrame(
            this ILogger logger,
            Transport transport,
            WSSocket.OpCode opCode,
            int size) =>
            _receivedWebSocketFrame(logger, transport, opCode, size, null!);

        internal static void LogSendingWebSocketFrame(
            this ILogger logger,
            Transport transport,
            WSSocket.OpCode opCode,
            int size) =>
            _sendingWebSocketFrame(logger, transport, opCode, size, null!);

        internal static void LogStartReceivingDatagrams(
            this ILogger logger,
            Transport transport,
            string localAddress,
            string peerAddress,
            IReadOnlyList<string> interfaces) =>
            _startReceivingDatagrams(logger, transport, localAddress, peerAddress, interfaces, null!);

        internal static void LogStartSendingDatagrams(
            this ILogger logger,
            Transport transport,
            string localAddress,
            string peerAddress,
            IReadOnlyList<string> interfaces) =>
            _startSendingDatagrams(logger, transport, localAddress, peerAddress, interfaces, null!);

        internal static void LogStartAcceptingConnections(this ILogger logger, Transport transport, IAcceptor acceptor) =>
            _startAcceptingConnections(logger, transport, acceptor, null!);

        internal static void LogStopAcceptingConnections(this ILogger logger, Transport transport, IAcceptor acceptor) =>
            _stopAcceptingConnections(logger, transport, acceptor, null!);

        internal static void LogPingEventHandlerException(this ILogger logger, Exception exception) =>
            _pingEventHanderException(logger, exception);

        internal static void LogReceivedData(this ILogger logger, int size, Transport transport) =>
            _receivedData(logger, size, transport, null!);

        internal static void LogSentData(this ILogger logger, int size, Transport transport) =>
            _sentData(logger, size, transport, null!);

        internal static void LogBindingSocketAttempt(this ILogger logger, Transport transport, string localAddress) =>
            _bindingSocketAttempt(logger, transport, localAddress, null!);

        internal static void LogDatagramSizeExceededIncomingFrameMaxSize(this ILogger logger, int size) =>
            _receivedDatagramExceededIncomingFrameMaxSize(logger, size, null!);

        internal static void LogDatagramConnectionReceiveCloseConnectionFrame(this ILogger logger) =>
            _datagramConnectionReceiveCloseConnectionFrame(logger, null!);

        internal static void LogReceiveBufferSizeAdjusted(
            this ILogger logger,
            Transport transport,
            int requestedSize,
            int adjustedSize) =>
            _receiveBufferSizeAdjusted(logger, transport, requestedSize, adjustedSize, null!);

        internal static void LogSendBufferSizeAdjusted(
            this ILogger logger,
            Transport transport,
            int requestedSize,
            int adjustedSize) =>
            _sendBufferSizeAdjusted(logger, transport, requestedSize, adjustedSize, null!);

        internal static void LogReceivedSlicFrame(
            this ILogger logger,
            SlicDefinitions.FrameType frameType,
            int frameSize,
            long? streamId)
        {
            switch (frameType)
            {
                case SlicDefinitions.FrameType.Initialize:
                    {
                        _receivedInitializeFrame(logger, frameSize, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.InitializeAck:
                    {
                        _receivedInitializeAckFrame(logger, frameSize, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.Version:
                    {
                        _receivedVersionFrame(logger, frameSize, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.Ping:
                    {
                        _receivedPingFrame(logger, frameSize, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.Pong:
                    {
                        _receivedPongFrame(logger, frameSize, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.Stream:
                    {
                        Debug.Assert(streamId != null);
                        _receivedStreamFrame(logger, frameSize, streamId!.Value, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.StreamLast:
                    {
                        Debug.Assert(streamId != null);
                        _receivedStreamLastFrame(logger, frameSize, streamId!.Value, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.StreamReset:
                    {
                        _receivedStreamResetFrame(logger, frameSize, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.StreamConsumed:
                    {
                        _receivedStreamConsumedFrame(logger, frameSize, null!);
                        break;
                    }
                default:
                    {
                        Debug.Assert(false);
                        break;
                    }
            }
        }

        internal static void LogSendingSlicFrame(
            this ILogger logger,
            SlicDefinitions.FrameType frameType,
            int frameSize,
            long? streamId)
        {
            switch (frameType)
            {
                case SlicDefinitions.FrameType.Initialize:
                    {
                        _sendingInitializeFrame(logger, frameSize, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.InitializeAck:
                    {
                        _sendingInitializeAckFrame(logger, frameSize, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.Version:
                    {
                        _sendingVersionFrame(logger, frameSize, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.Ping:
                    {
                        _sendingPingFrame(logger, frameSize, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.Pong:
                    {
                        _sendingPongFrame(logger, frameSize, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.Stream:
                    {
                        Debug.Assert(streamId != null);
                        _sendingStreamFrame(logger, frameSize, streamId!.Value, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.StreamLast:
                    {
                        Debug.Assert(streamId != null);
                        _sendingStreamLastFrame(logger, frameSize, streamId!.Value, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.StreamReset:
                    {
                        _sendingStreamResetFrame(logger, frameSize, null!);
                        break;
                    }
                case SlicDefinitions.FrameType.StreamConsumed:
                    {
                        _sendingStreamConsumedFrame(logger, frameSize, null!);
                        break;
                    }
                default:
                    {
                        Debug.Assert(false);
                        break;
                    }
            }
        }

        internal static IDisposable StartSocketScope(
            this ILogger logger,
            Transport transport,
            string localAddress,
            string remoteAddress) =>
            _socketScope(logger, transport, localAddress, remoteAddress);

        internal static IDisposable StartDatagramSocketScope(
            this ILogger logger,
            Transport transport,
            string localAddress,
            string remoteAddress,
            IReadOnlyList<string> interfaces) =>
            _datagramSocketScope(logger, transport, localAddress, remoteAddress, interfaces);

        internal static IDisposable StartMulticastSocketScope(
            this ILogger logger,
            Transport transport,
            string localAddress,
            string multicastAddress,
            IReadOnlyList<string> interfaces) =>
            _multicastSocketScope(logger, transport, localAddress, multicastAddress, interfaces);
    }
}
