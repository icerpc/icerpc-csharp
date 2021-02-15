// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ZeroC.Ice
{
    internal static class TransportLoggerExtensions
    {
        private static readonly Action<ILogger, Transport, Exception> _acceptingConnection =
            LoggerMessage.Define<Transport>(
                LogLevel.Debug,
                GetEventId(TransportEvent.AcceptingConnection),
                "accepted {Transport} connection");

        private static readonly Action<ILogger, Transport, Exception> _acceptingConnectionFailed =
            LoggerMessage.Define<Transport>(
                LogLevel.Error,
                GetEventId(TransportEvent.AcceptingConnectionFailed),
                "failed to accept {Transport} connection");

        private static readonly Action<ILogger, string, MultiStreamSocket, Exception> _connectionAccepted =
            LoggerMessage.Define<string, MultiStreamSocket>(
                LogLevel.Debug,
                GetEventId(TransportEvent.ConnectionAccepted),
                "accepted {Transport} connection {Connectionn}");

        private static readonly Action<ILogger, string, MultiStreamSocket, Exception> _connectionEstablished =
            LoggerMessage.Define<string, MultiStreamSocket>(
                LogLevel.Debug,
                GetEventId(TransportEvent.ConnectionEstablished),
                "established {Transport} connection {Connectionn}");

        private static readonly Action<ILogger, Connection, Exception> _connectionCallbackException =
            LoggerMessage.Define<Connection>(
                LogLevel.Error,
                GetEventId(TransportEvent.ConnectionCallbackException),
                "connection callback exception {Connection}");

        private static readonly Action<ILogger, string, MultiStreamSocket, Exception> _connectionClosed =
            LoggerMessage.Define<string, MultiStreamSocket>(
                LogLevel.Debug,
                GetEventId(TransportEvent.ConnectionClosed),
                "closed {Transport} connection {Connection}");

        private static readonly Action<ILogger, Connection, Exception> _connectionException =
            LoggerMessage.Define<Connection>(
                LogLevel.Error,
                GetEventId(TransportEvent.ConnectionException),
                "connection exception {Connection}");

        private static readonly Action<ILogger, string, WSSocket, Exception> _httpUpgradeRequestAccepted =
            LoggerMessage.Define<string, WSSocket>(
                LogLevel.Error,
                GetEventId(TransportEvent.HttpUpgradeRequestFailed),
                "accepted {Transport} connection HTTP upgrade request {Socket}");

        private static readonly Action<ILogger, string, WSSocket, Exception> _httpUpgradeRequestFailed =
            LoggerMessage.Define<string, WSSocket>(
                LogLevel.Error,
                GetEventId(TransportEvent.HttpUpgradeRequestFailed),
                "{Transport} connection HTTP upgrade request failed {Socket}");

        private static readonly Action<ILogger, string, WSSocket, Exception> _httpUpgradeRequestSucceed =
            LoggerMessage.Define<string, WSSocket>(
                LogLevel.Debug,
                GetEventId(TransportEvent.HttpUpgradeRequestSucceed),
                "{Transport} connection HTTP upgrade request succee {Socket}");

        private static readonly Action<ILogger, int, Exception> _receivedInvalidDatagram =
            LoggerMessage.Define<int>(
                LogLevel.Error,
                GetEventId(TransportEvent.ReceivedInvalidDatagram),
                "received datagram with {Bytes} bytes");

        private static readonly Action<ILogger, string, IReadOnlyList<Endpoint>, Exception> _objectAdapterPublishedEndpoints =
            LoggerMessage.Define<string, IReadOnlyList<Endpoint>>(
                LogLevel.Information,
                GetEventId(TransportEvent.ObjectAdapterPublishedEndpoints),
                "published endpoints for object adapter {Name}: {Endpoints}");

        private static readonly Action<ILogger, string, WSSocket.OpCode, int, WSSocket, Exception> _receivedWebSocketFrame =
            LoggerMessage.Define<string, WSSocket.OpCode, int, WSSocket>(
                LogLevel.Error,
                GetEventId(TransportEvent.ReceivedWebSocketFrame),
                "received {Transport} {OpCode} frame with {Size} bytes payload {Socket}");

        private static readonly Action<ILogger, string, WSSocket.OpCode, int, WSSocket, Exception> _sendingWebSocketFrame =
            LoggerMessage.Define<string, WSSocket.OpCode, int, WSSocket>(
                LogLevel.Error,
                GetEventId(TransportEvent.SendingWebSocketFrame),
                "sending {Transport} {OpCode} frame with {Size} bytes payload {Socket}");

        private static readonly Action<ILogger, string, IAcceptor, Exception> _startAcceptingConnections =
            LoggerMessage.Define<string, IAcceptor>(
                LogLevel.Information,
                GetEventId(TransportEvent.StartAcceptingConnections),
                "start accepting {TransportName} connections at {Acceptor}");

        private static readonly Action<ILogger, string, IAcceptor, Exception> _stopAcceptingConnections =
            LoggerMessage.Define<string, IAcceptor>(
                LogLevel.Information,
                GetEventId(TransportEvent.StartAcceptingConnections),
                "stop accepting {TransportName} connections at {Acceptor}");

        private static readonly Action<ILogger, Exception> _pingEventHanderException = LoggerMessage.Define(
            LogLevel.Error,
            GetEventId(TransportEvent.PingEventHandlerException),
            "ping event handler raised an exception");

        private static readonly Action<ILogger, int, string, Exception> _receivedData =
            LoggerMessage.Define<int, string>(
                LogLevel.Debug,
                GetEventId(TransportEvent.ReceivedData),
                "received {Size} bytes via {Transport}");

        private static readonly Action<ILogger, int, string, Exception> _sentData = LoggerMessage.Define<int, string>(
            LogLevel.Debug,
            GetEventId(TransportEvent.SentData),
            "sent {Size} bytes via {TransportName}");

        private static readonly Action<ILogger, string, SingleStreamSocket, Exception> _bindingSocketAttempt =
            LoggerMessage.Define<string, SingleStreamSocket>(
                LogLevel.Debug,
                GetEventId(TransportEvent.BindingSocketAttempt),
                "attempting to bind to {TransportName} socket {Socket}");

        private static readonly Action<ILogger, string, MultiStreamSocket, Exception> _startReceivingDatagrams =
            LoggerMessage.Define<string, MultiStreamSocket>(
                LogLevel.Debug,
                GetEventId(TransportEvent.StartReceivingDatagrams),
                "starting to receive {TransportName} datagrams {Socket}");

        private static readonly Action<ILogger, string, MultiStreamSocket, Exception> _startSendingDatagrams =
            LoggerMessage.Define<string, MultiStreamSocket>(
                LogLevel.Debug,
                GetEventId(TransportEvent.StartSendingDatagrams),
                "starting to send {TransportName} datagrams {Socket}");

        private static readonly Action<ILogger, int, Exception> _receivedDatagramExceededIncomingFrameMaxSize =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                GetEventId(TransportEvent.DatagramSizeExceededIncomingFrameMaxSize),
                "frame with {Size} bytes exceeds Ice.IncomingFrameMaxSize value");

        private static readonly Action<ILogger, int, Exception> _maximumDatagramSizeExceeded =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                GetEventId(TransportEvent.MaximumDatagramSizeExceeded),
                "maximum datagram size of {Size} exceeded");

        private static readonly Action<ILogger, Exception> _datagramConnectionReceiveCloseConnectionFrame =
            LoggerMessage.Define(
                LogLevel.Debug,
                GetEventId(TransportEvent.DatagramConnectionReceiveCloseConnectionFrame),
                "ignoring close connection frame for datagram connection");

        private static readonly Action<ILogger, Transport, int, int, Exception> _receiveBufferSizeAdjusted =
            LoggerMessage.Define<Transport, int, int>(
                LogLevel.Debug,
                GetEventId(TransportEvent.ReceiveBufferSizeAdjusted),
                "{Transport} receive buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}");

        private static readonly Action<ILogger, Transport, int, int, Exception> _sendBufferSizeAdjusted =
            LoggerMessage.Define<Transport, int, int>(
                LogLevel.Debug,
                GetEventId(TransportEvent.SendBufferSizeAdjusted),
                "{Transport} send buffer size: requested size of {RequestedSize} adjusted to {AdjustedSize}");

        private static readonly Action<ILogger, Encoding, Exception> _sendIce1ValidateConnectionFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                GetEventId(TransportEvent.SendIce1ValidateConnectionFrame),
                "sent ice1 validate connection frame: encoding = `{Encoding}'");

        private static readonly Action<ILogger, int, Exception> _receivedIce1RequestBatchFrame =
            LoggerMessage.Define<int>(
                LogLevel.Information,
                GetEventId(TransportEvent.ReceivedIce1RequestBatchFrame),
                "received ice1 request batch frame: number of requests = `{Requests}'");

        private static readonly Action<ILogger, string, string, string, bool, SortedDictionary<string, string>, Exception> _receivedIce1RequestFrame =
            LoggerMessage.Define<string, string, string, bool, SortedDictionary<string, string>>(
                LogLevel.Information,
                GetEventId(TransportEvent.ReceivedIce1RequestFrame),
                "received ice1 request frame: operation = {Operation}, identity = {Identity}, facet = {Facet}, " +
                "idempotent = {Idempotent}, context = {Context}");

        private static readonly Action<ILogger, string, string, string, bool, IReadOnlyDictionary<string, string>, Exception> _sendingIce1RequestFrame =
            LoggerMessage.Define<string, string, string, bool, IReadOnlyDictionary<string, string>>(
                LogLevel.Debug,
                GetEventId(TransportEvent.SendingIce1RequestFrame),
                "sending ice1 request frame: operation = {Operation}, identity = {Identity}, facet = {Facet}, " +
                "idempotent = {Idempotent}, context = {Context}");

        private static readonly Func<ILogger, Encoding, int, (int, string), IDisposable> _ice1RequestsScope =
            LoggerMessage.DefineScope<Encoding, int, (int, string)>(
                "request: encoding {Encoding}, frame size = {FrameSize}, request ID = {RequestID}");

        private static readonly Action<ILogger, ResultType, (int, string), Exception> _receivedIce1ResponseFrame =
            LoggerMessage.Define<ResultType, (int, string)>(
                LogLevel.Debug,
                GetEventId(TransportEvent.ReceivedIce1ResponseFrame),
                "received ice1 response frame: result = {Result}, request ID = {RequestID}");

        private static readonly Action<ILogger, ResultType, (int, string), Exception> _sendingIce1ResponseFrame =
            LoggerMessage.Define<ResultType, (int, string)>(
                LogLevel.Debug,
                GetEventId(TransportEvent.SendingIce1ResponseFrame),
                "sending ice1 response frame: result = {Result}, request ID = {RequestID}");

        private static readonly Action<ILogger, string, string, string, bool, SortedDictionary<string, string>, Exception> _receivedIce2RequestFrame =
            LoggerMessage.Define<string, string, string, bool, SortedDictionary<string, string>>(
                LogLevel.Information,
                GetEventId(TransportEvent.ReceivedIce2RequestFrame),
                "received ice2 request frame: operation = {Operation}, identity = {Identity}, facet = {Facet}, " +
                "idempotent = {Idempotent}, context = {Context}");

        private static readonly Func<ILogger, string, Transport, Protocol, IDisposable> _collocatedAcceptorScope =
            LoggerMessage.DefineScope<string, Transport, Protocol>(
                "acceptor: adapter = {AdapterName}, transport = {Transport}, protocol = {Protocol}");

        private static readonly Func<ILogger, string, string, Transport, Protocol, IDisposable> _tcpAcceptorScope =
            LoggerMessage.DefineScope<string, string, Transport, Protocol>(
                "acceptor: adapter = {AdapterName}, local address = {LocalAddress}, transport = {Transport}, protocol = {Protocol}");

        private static readonly Action<ILogger, string, string, string, bool, IReadOnlyDictionary<string, string>, Exception> _sendingIce2RequestFrame =
            LoggerMessage.Define<string, string, string, bool, IReadOnlyDictionary<string, string>>(
                LogLevel.Information,
                GetEventId(TransportEvent.SendingIce2RequestFrame),
                "sending ice2 request frame: operation = {Operation}, identity = {Identity}, facet = {Facet}, " +
                "idempotent = {Idempotent}, context = {Context}");

        private static readonly Func<ILogger, Encoding, int, (long, string), IDisposable> _ice2RequestScope =
            LoggerMessage.DefineScope<Encoding, int, (long, string)>(
                "request: encoding {Encoding}, frame size = {FrameSize}, stream ID = {StreamID}");

        private static readonly Action<ILogger, ResultType, (long, string), Exception> _receivedIce2ResponseFrame =
            LoggerMessage.Define<ResultType, (long, string)>(
                LogLevel.Debug,
                GetEventId(TransportEvent.ReceivedIce2ResponseFrame),
                "received ice2 response frame: result = {Result}, stream ID = {StreamID}");

        private static readonly Action<ILogger, ResultType, (long, string), Exception> _sendingIce2ResponseFrame =
            LoggerMessage.Define<ResultType, (long, string)>(
                LogLevel.Debug,
                GetEventId(TransportEvent.SendingIce2ResponseFrame),
                "sending ice2 response frame: result = {Result}, stream ID = {StreamID}");

        private static readonly Action<ILogger, Encoding, Exception> _receivedIce2GoAwayFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                GetEventId(TransportEvent.ReceivedIce2GoAwayFrame),
                "received ice2 go away frame: encoding = {Encoding}");

        private static readonly Action<ILogger, Encoding, Exception> _receivedIce1CloseConnectionFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                GetEventId(TransportEvent.ReceivedIce1CloseConnectionFrame),
                "received ice1 close connection frame: encoding = {Encoding}");

        private static readonly Action<ILogger, Encoding, Exception> _receivedIce1ValidateConnectionFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                GetEventId(TransportEvent.ReceivedIce1ValidateConnectionFrame),
                "received ice1 validate connection frame: encoding = {Encoding}");

        private static readonly Action<ILogger, Encoding, Exception> _receivedIce2InitializeFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                GetEventId(TransportEvent.ReceivedIce2InitializeFrame),
                "received ice2 initialize frame: encoding = {Encoding}");

        private static readonly Action<ILogger, Encoding, Exception> _sendingIce1CloseConnectionFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                GetEventId(TransportEvent.SendingIce1CloseConnectionFrame),
                "sending ice1 close connection frame: encoding = {Encoding}");

        private static readonly Action<ILogger, Encoding, Exception> _sendingIce2GoAwayFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                GetEventId(TransportEvent.SendingIce2GoAwayFrame),
                "sending ice2 go away frame: encoding = {Encoding}");

        private static readonly Action<ILogger, Encoding, Exception> _sendingIce2InitializeFrame =
            LoggerMessage.Define<Encoding>(
                LogLevel.Debug,
                GetEventId(TransportEvent.SendingIce2InitializeFrame),
                "sending ice2 initialize frame: encoding = {Encoding}");

        internal static void LogAcceptingConnection(this ILogger logger, Transport transport) =>
            _acceptingConnection(logger, transport, null!);

        internal static void LogAcceptingConnectionFailed(this ILogger logger, Transport transport, Exception ex) =>
            _acceptingConnectionFailed(logger, transport, ex);

        internal static void LogConnectionCallbackException(
            this ILogger logger,
            Connection connection,
            Exception ex) =>
            _connectionCallbackException(logger, connection, ex);

        internal static void LogConnectionAccepted(
            this ILogger logger,
            string transportName,
            MultiStreamSocket socket) =>
            _connectionAccepted(logger, transportName, socket, null!);

        internal static void LogConnectionClosed(
            this ILogger logger,
            string transportName,
            MultiStreamSocket socket,
            Exception? exception = null) =>
            _connectionClosed(logger, transportName, socket, exception!);

        internal static void LogConnectionEstablished(
            this ILogger logger,
            string transportName,
            MultiStreamSocket socket) =>
            _connectionEstablished(logger, transportName, socket, null!);

        internal static void LogConnectionException(
            this ILogger logger,
            Connection connection,
            Exception ex) =>
            _connectionException(logger, connection, ex);

        internal static void LogHttpUpgradeRequestAccepted(
            this ILogger logger,
            string transport,
            WSSocket socket) =>
            _httpUpgradeRequestAccepted(logger, transport, socket, null!);

        internal static void LogHttpUpgradeRequestFailed(
            this ILogger logger,
            string transport,
            WSSocket socket,
            Exception ex) =>
            _httpUpgradeRequestFailed(logger, transport, socket, ex);

        internal static void LogHttpUpgradeRequestSucceed(
            this ILogger logger,
            string transport,
            WSSocket socket) =>
            _httpUpgradeRequestSucceed(logger, transport, socket, null!);

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
            string transport,
            WSSocket.OpCode opCode,
            int size,
            WSSocket socket) =>
            _receivedWebSocketFrame(logger, transport, opCode, size, socket, null!);

        internal static void LogSendingWebSocketFrame(
            this ILogger logger,
            string transport,
            WSSocket.OpCode opCode,
            int size,
            WSSocket socket) =>
            _sendingWebSocketFrame(logger, transport, opCode, size, socket, null!);

        internal static void LogStartReceivingDatagrams(this ILogger logger, string transport, MultiStreamSocket socket) =>
            _startReceivingDatagrams(logger, transport, socket, null!);

        internal static void LogStartSendingDatagrams(this ILogger logger, string transport, MultiStreamSocket socket) =>
            _startSendingDatagrams(logger, transport, socket, null!);

        internal static void LogStartAcceptingConnections(this ILogger logger, string transport, IAcceptor acceptor) =>
            _startAcceptingConnections(logger, transport, acceptor, null!);

        internal static void LogStopAcceptingConnections(this ILogger logger, string transport, IAcceptor acceptor) =>
            _stopAcceptingConnections(logger, transport, acceptor, null!);

        internal static void LogPingEventHandlerException(this ILogger logger, Exception exception) =>
            _pingEventHanderException(logger, exception);

        internal static void LogReceivedData(this ILogger logger, int size, string transport) =>
            _receivedData(logger, size, transport, null!);

        internal static void LogSentData(this ILogger logger, int size, string transport) =>
            _sentData(logger, size, transport, null!);

        internal static void LogBindingSocketAttempt(this ILogger logger, string transport, SingleStreamSocket socket) =>
            _bindingSocketAttempt(logger, transport, socket, null!);

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

        internal static void LogSendIce1ValidateConnectionFrame(this ILogger logger) =>
            _sendIce1ValidateConnectionFrame(logger, Ice1Definitions.Encoding, null!);

        internal static void LogReceivedIce1RequestBatchFrame(this ILogger logger, int requests) =>
            _receivedIce1RequestBatchFrame(logger, requests, null!);

        internal static IDisposable StartRequestScope(this ILogger logger, long streamId, IncomingRequestFrame request)
        {
            Debug.Assert(logger.IsEnabled(LogLevel.Critical));

            if (request.Protocol == Protocol.Ice1)
            {
                return _ice1RequestsScope(logger,
                                          request.PayloadEncoding,
                                          request.PayloadSize,
                                          GetIce1RequestID(streamId));
            }
            else
            {
                return _ice2RequestScope(logger,
                                         request.PayloadEncoding,
                                         request.PayloadSize,
                                         GetIce2StreamID(streamId));
            }
        }

        internal static IDisposable StartRequestScope(this ILogger logger, long streamId, OutgoingRequestFrame request)
        {
            Debug.Assert(logger.IsEnabled(LogLevel.Critical));

            if (request.Protocol == Protocol.Ice1)
            {
                return _ice1RequestsScope(logger,
                                          request.PayloadEncoding,
                                          request.PayloadSize,
                                          GetIce1RequestID(streamId));
            }
            else
            {
                return _ice2RequestScope(logger,
                                         request.PayloadEncoding,
                                         request.PayloadSize,
                                         GetIce2StreamID(streamId));
            }
        }

        internal static void LogReceivedRequest(this ILogger logger, IncomingRequestFrame request)
        {
            if (request.Protocol == Protocol.Ice1)
            {
                _receivedIce1RequestFrame(logger,
                                          request.Operation,
                                          request.Identity.ToString(ToStringMode.Unicode),
                                          request.Facet,
                                          request.IsIdempotent,
                                          request.Context,
                                          null!);
            }
            else
            {
                _receivedIce2RequestFrame(logger,
                                          request.Operation,
                                          request.Identity.ToString(ToStringMode.Unicode),
                                          request.Facet,
                                          request.IsIdempotent,
                                          request.Context,
                                          null!);
            }
        }

        internal static void LogSendingRequest(this ILogger logger, OutgoingRequestFrame request)
        {
            if (request.Protocol == Protocol.Ice1)
            {
                _sendingIce1RequestFrame(logger,
                                         request.Operation,
                                         request.Identity.ToString(ToStringMode.Unicode),
                                         request.Facet,
                                         request.IsIdempotent,
                                         request.Context,
                                         null!);
            }
            else
            {
                _sendingIce2RequestFrame(logger,
                                         request.Operation,
                                         request.Identity.ToString(ToStringMode.Unicode),
                                         request.Facet,
                                         request.IsIdempotent,
                                         request.Context,
                                         null!);
            }
        }

        internal static void LogSendingResponse(this ILogger logger, long streamId, OutgoingResponseFrame response)
        {
            if (response.Protocol == Protocol.Ice1)
            {
                _sendingIce1ResponseFrame(logger, response.ResultType, GetIce1RequestID(streamId), null!);
            }
            else
            {
                _sendingIce2ResponseFrame(logger, response.ResultType, GetIce2StreamID(streamId), null!);
            }
        }

        internal static void LogReceivedResponse(this ILogger logger, long streamId, IncomingResponseFrame response)
        {
            if (response.Protocol == Protocol.Ice1)
            {
                _receivedIce1ResponseFrame(logger, response.ResultType, GetIce1RequestID(streamId), null!);
            }
            else
            {
                _receivedIce2ResponseFrame(logger, response.ResultType, GetIce2StreamID(streamId), null!);
            }
        }

        internal static void LogSendingResponse(this ILogger logger, long streamId, IncomingResponseFrame response)
        {
            if (response.Protocol == Protocol.Ice1)
            {
                _receivedIce1ResponseFrame(logger, response.ResultType, GetIce1RequestID(streamId), null!);
            }
            else
            {
                _receivedIce2ResponseFrame(logger, response.ResultType, GetIce2StreamID(streamId), null!);
            }
        }

        internal static void LogReceivedIce2GoAwayFrame(this ILogger logger) =>
            _receivedIce2GoAwayFrame(logger, Ice2Definitions.Encoding, null!);

        internal static void LogReceivedIce1CloseConnectionFrame(this ILogger logger) =>
            _receivedIce1CloseConnectionFrame(logger, Ice1Definitions.Encoding, null!);

        internal static void LogReceivedIce1ValidateConnectionFrame(this ILogger logger) =>
            _receivedIce1ValidateConnectionFrame(logger, Ice1Definitions.Encoding, null!);

        internal static void LogReceivedIce2InitializeFrame(this ILogger logger) =>
            _receivedIce2InitializeFrame(logger, Ice2Definitions.Encoding, null!);

        internal static void LogSendingIce1CloseConnectionFrame(this ILogger logger) =>
            _sendingIce1CloseConnectionFrame(logger, Ice1Definitions.Encoding, null!);

        internal static void LogSendingIce2GoAwayFrame(this ILogger logger) =>
            _sendingIce2GoAwayFrame(logger, Ice2Definitions.Encoding, null!);

        internal static void LogSendingIce2InitializeFrame(this ILogger logger) =>
            _sendingIce2InitializeFrame(logger, Ice2Definitions.Encoding, null!);

        internal static IDisposable? StartCollocatedAcceptorScope(
            this ILogger logger,
            string adapterName,
            Transport transport,
            Protocol protocol) =>
            _collocatedAcceptorScope(logger, adapterName, transport, protocol);

        internal static IDisposable? StartTcpAcceptorScope(
            this ILogger logger,
            string adapterName,
            string locallAdress,
            Transport transport,
            Protocol protocol) =>
            _tcpAcceptorScope(logger, adapterName, locallAdress, transport, protocol);

        private static (int, string) GetIce1RequestID(long streamId)
        {
            int requestId = streamId % 4 < 2 ? (int)(streamId >> 2) + 1 : 0;
            return (requestId, requestId == 0 ? "oneway" : "");
        }

        private static (long, string) GetIce2StreamID(long streamId)
        {
            string description = (streamId % 4) switch
            {
                0 => "(client-initiated, bidirectional)",
                1 => "(server-initiated, bidirectional)",
                2 => "(client-initiated, unidirectional)",
                3 => "(server-initiated, unidirectional)",
                _ => throw new InvalidArgumentException(nameof(streamId))
            };
            return (streamId, description);
        }

        private static EventId GetEventId(TransportEvent e) => new EventId((int)e, e.ToString());

        private enum TransportEvent
        {
            AcceptingConnection,
            AcceptingConnectionFailed,
            ConnectionAccepted,
            ConnectionEstablished,
            ConnectionException,
            ConnectionCallbackException,
            ConnectionClosed,
            HttpUpgradeRequestAccepted,
            HttpUpgradeRequestFailed,
            HttpUpgradeRequestSucceed,
            MaxDatagramSizeExceed,
            ObjectAdapterPublishedEndpoints,
            ObjectAdapaterUnknownProperties,
            ReceivedInvalidDatagram,
            ReceivedWebSocketFrame,
            SendingWebSocketFrame,
            StartAcceptingConnections,
            StopAcceptingConnections,
            StartReceivingDatagrams,
            StartSendingDatagrams,
            BindingSocketAttempt,
            PingEventHandlerException,
            ReceivedData,
            DatagramSizeExceededIncomingFrameMaxSize,
            DatagramConnectionReceiveCloseConnectionFrame,
            MaximumDatagramSizeExceeded,
            SentData,
            ReceiveBufferSizeAdjusted,
            SendBufferSizeAdjusted,

            SendIce1ValidateConnectionFrame,

            ReceivedIce1RequestBatchFrame,
            ReceivedIce1RequestFrame,
            ReceivedIce1ResponseFrame,
            ReceivedIce1CloseConnectionFrame,
            ReceivedIce1ValidateConnectionFrame,

            ReceivedIce2RequestFrame,
            ReceivedIce2ResponseFrame,

            ReceivedIce2GoAwayFrame,
            ReceivedIce2InitializeFrame,

            SendingIce1CloseConnectionFrame,
            SendingIce1RequestFrame,
            SendingIce1ResponseFrame,

            SendingIce2RequestFrame,
            SendingIce2ResponseFrame,
            SendingIce2GoAwayFrame,
            SendingIce2InitializeFrame,
        }
    }

    internal static class SlicingLoggerExceptions
    {
        private static readonly Action<ILogger, string, string, Exception> _slicingUnknowType = LoggerMessage.Define<string, string>(
            LogLevel.Warning,
            GetEventId(SlicingEvent.SlicingUnknownType),
            "slicing unknown {Kind} type `{PrintableId}'");

        internal static void LogSlicingUnknownType(this ILogger logger, string kind, string printableId) =>
            _slicingUnknowType(logger, kind, printableId, null!);
        private static EventId GetEventId(SlicingEvent e) => new EventId((int)e, e.ToString());

        private enum SlicingEvent
        {
            SlicingUnknownType
        }
    }

    internal static class DefaultLoggerExtensions
    {
        private static readonly Action<ILogger, string, Exception> _deprecatedProperty = LoggerMessage.Define<string>(
            LogLevel.Warning,
            GetEventId(Event.DeprecatedProperty),
            "deprecated property {Property}");

        private static readonly Action<ILogger, string, string, Exception> _deprecatedPropertyBy =
            LoggerMessage.Define<string, string>(
                LogLevel.Warning,
                GetEventId(Event.DeprecatedProperty),
                "deprecated property {DeprecatedProperty} deprecated by: {NewProperty}");

        private static readonly Action<ILogger, string, Exception> _unknownProperty = LoggerMessage.Define<string>(
            LogLevel.Warning,
            GetEventId(Event.UnknownProperty),
            "unknown property {Property}");

        private static readonly Action<ILogger, string, IReadOnlyList<string>, Exception> _unknownProxyProperty =
            LoggerMessage.Define<string, IReadOnlyList<string>>(
                LogLevel.Warning,
                GetEventId(Event.UnknownProperty),
                "found unknown properties {Properties} for proxy {Proxy}");

        internal static void LogDeprecatedProperty(this ILogger logger, string property) =>
            _deprecatedProperty(logger, property, null!);

        internal static void LogDeprecatedPropertyBy(this ILogger logger, string deprecatedProperty, string newProperty) =>
            _deprecatedPropertyBy(logger, deprecatedProperty, newProperty, null!);

        internal static void LogUnknownProperty(this ILogger logger, string property) =>
            _unknownProperty(logger, property, null!);

        internal static void LogUnknownProxyProperty(this ILogger logger, string proxy, IReadOnlyList<string> properties) =>
            _unknownProxyProperty(logger, proxy, properties, null!);

        private static EventId GetEventId(Event e) => new EventId((int)e, e.ToString());

        private enum Event
        {
            DeprecatedProperty,
            DeprecatedPropertyBy,
            UnknownProperty,
            UnknownProxyProperty
        }
    }

    internal static class RetryLoggerExtensions
    {
        private static readonly Action<ILogger, RetryPolicy, int, int, Exception> _retryRequest =
            LoggerMessage.Define<RetryPolicy, int, int>(
                LogLevel.Debug,
                GetEventId(Event.RetryRequest),
                "retrying request because of retryable exception: retry policy = {RetryPolicy}, " +
                "request attempt = {Attempt} / {MaxAttempts}");

        private static readonly Action<ILogger, RetryPolicy, int, int, Exception> _retryConnectionEstablishment =
            LoggerMessage.Define<RetryPolicy, int, int>(
                LogLevel.Debug,
                GetEventId(Event.RetryConnectionEstablishment),
                "retrying connection establishment because of retryable exception: retry policy = {RetryPolicy}, " +
                "request attempt = {Attempt} / {MaxAttempts}");

        private static readonly Action<ILogger, Exception> _retryConnectionEstablishment1 = LoggerMessage.Define(
            LogLevel.Debug,
            GetEventId(Event.RetryConnectionEstablishment),
            "retrying connection establishment because of retryable exception");

        internal static void LogRetryRequestInvocation(
            this ILogger logger,
            RetryPolicy retryPolicy,
            int attempt,
            int maxAttempts,
            Exception? ex) =>
            _retryRequest(logger, retryPolicy, attempt, maxAttempts, ex!);

        // TODO trace remote exception, currently we pass null because the remote exception is not unmarshaled at this point
        internal static void LogRetryConnectionEstablishment(
            this ILogger logger,
            RetryPolicy retryPolicy,
            int attempt,
            int maxAttempts,
            Exception? ex) =>
            _retryConnectionEstablishment(logger, retryPolicy, attempt, maxAttempts, ex!);

        internal static void LogRetryConnectionEstablishment(this ILogger logger, Exception? ex) =>
            _retryConnectionEstablishment1(logger, ex!);

        private static EventId GetEventId(Event e) => new EventId((int)e, e.ToString());

        private enum Event
        {
            RetryRequest,
            RetryConnectionEstablishment
        }
    }

    internal static class SlicLoggerExtensions
    {
        private static readonly Action<ILogger, int, Exception> _receivedInitializeFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            GetEventId(SlicEvent.ReceivedInitializeFrame),
            "received Slic initialize frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _receivedInitializeAckFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            GetEventId(SlicEvent.ReceivedInitializeAckFrame),
            "received Slic initialize ack frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _receivedVersionFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            GetEventId(SlicEvent.ReceivedVersionFrame),
            "received Slic version frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _receivedPingFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            GetEventId(SlicEvent.ReceivedPingFrame),
            "received Slic ping frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _receivedPongFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            GetEventId(SlicEvent.ReceivedPongFrame),
            "received Slic pong frame: size = {Size}");

        private static readonly Action<ILogger, int, long, Exception> _receivedStreamFrame =
            LoggerMessage.Define<int, long>(
                LogLevel.Debug,
                GetEventId(SlicEvent.ReceivedStreamFrame),
                "received Slic stream frame: size = {Size}, streamId = {StreamId}");

        private static readonly Action<ILogger, int, long, Exception> _receivedStreamLastFrame =
            LoggerMessage.Define<int, long>(
                LogLevel.Debug,
                GetEventId(SlicEvent.ReceivedStreamLastFrame),
                "received Slic stream last frame: size = {Size}, streamId = {StreamId}");

        private static readonly Action<ILogger, int, Exception> _receivedStreamResetFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            GetEventId(SlicEvent.ReceivedPongFrame),
            "received Slic stream reset frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _receivedStreamConsumedFrame =
            LoggerMessage.Define<int>(
                LogLevel.Debug,
                GetEventId(SlicEvent.ReceivedStreamConsumedFrame),
                "received Slic stream consumed frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _sendingInitializeFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            GetEventId(SlicEvent.SendingInitializeFrame),
            "sending Slic initialize frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _sendingInitializeAckFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            GetEventId(SlicEvent.SendingInitializeAckFrame),
            "sending Slic initialize ack frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _sendingVersionFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            GetEventId(SlicEvent.SendingVersionFrame),
            "sending Slic version frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _sendingPingFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            GetEventId(SlicEvent.SendingPingFrame),
            "sending Slic ping frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _sendingPongFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            GetEventId(SlicEvent.SendingPongFrame),
            "sending Slic pong frame: size = {Size}");

        private static readonly Action<ILogger, int, long, Exception> _sendingStreamFrame =
            LoggerMessage.Define<int, long>(
                LogLevel.Debug,
                GetEventId(SlicEvent.SendingStreamFrame),
                "sending Slic stream frame: size = {Size}, streamId = {StreamId}");

        private static readonly Action<ILogger, int, long, Exception> _sendingStreamLastFrame =
            LoggerMessage.Define<int, long>(
                LogLevel.Debug,
                GetEventId(SlicEvent.SendingStreamLastFrame),
                "sending Slic stream last frame: size = {Size}, streamId = {StreamId}");

        private static readonly Action<ILogger, int, Exception> _sendingStreamResetFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            GetEventId(SlicEvent.SendingStreamResetFrame),
            "sending Slic stream reset frame: size = {Size}");

        private static readonly Action<ILogger, int, Exception> _sendingStreamConsumedFrame = LoggerMessage.Define<int>(
            LogLevel.Debug,
            GetEventId(SlicEvent.SendingStreamConsumedFrame),
            "sending Slic stream consumed frame: size = {Size}");

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
                    _sendingStreamFrame(logger, frameSize, streamId!.Value,  null!);
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

        private static EventId GetEventId(SlicEvent e) => new EventId((int)e, e.ToString());
        private enum SlicEvent
        {
            ReceivedInitializeFrame,
            ReceivedInitializeAckFrame,
            ReceivedVersionFrame,
            ReceivedPingFrame,
            ReceivedPongFrame,
            ReceivedStreamFrame,
            ReceivedStreamLastFrame,
            ReceivedStreamResetFrame,
            ReceivedStreamConsumedFrame,

            SendingInitializeFrame,
            SendingInitializeAckFrame,
            SendingVersionFrame,
            SendingPingFrame,
            SendingPongFrame,
            SendingStreamFrame,
            SendingStreamLastFrame,
            SendingStreamResetFrame,
            SendingStreamConsumedFrame
        }
    }
}