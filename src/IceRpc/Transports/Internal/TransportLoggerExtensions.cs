// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    /// <summary>This class contains ILogger extension methods for logging transport messages.</summary>
    internal static partial class TransportLoggerExtensions
    {
        private static readonly Func<ILogger, string, IDisposable> _listenerScope =
            LoggerMessage.DefineScope<string>("server(Endpoint={Server})");

        private static readonly Func<ILogger, bool, string, string, IDisposable> _connectionScope =
            LoggerMessage.DefineScope<bool, string, string>(
                "connection(IsServer={IsServer}, LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint})");

        private static readonly Func<ILogger, long, string, string, IDisposable> _streamScope =
            LoggerMessage.DefineScope<long, string, string>("stream(ID={ID}, InitiatedBy={InitiatedBy}, Kind={Kind})");

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectionAccepted,
            EventName = nameof(TransportEventIds.ConnectionAccepted),
            Level = LogLevel.Debug,
            Message = "accepted connection")]
        internal static partial void LogConnectionAccepted(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEventIds.SocketNetworkConnectionAccepted,
            EventName = nameof(TransportEventIds.SocketNetworkConnectionAccepted),
            Level = LogLevel.Debug,
            Message = "accepted connection (ReceiveBufferSize={RcvSize}, SendBufferSize={SndSize})")]
        internal static partial void LogSocketNetworkConnectionAccepted(this ILogger logger, int rcvSize, int sndSize);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectionAcceptFailed,
            EventName = nameof(TransportEventIds.ConnectionAcceptFailed),
            Level = LogLevel.Debug,
            Message = "failed to accept connection")]
        internal static partial void LogConnectionAcceptFailed(this ILogger logger, Exception? exception);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectionClosed,
            EventName = nameof(TransportEventIds.ConnectionClosed),
            Level = LogLevel.Debug,
            Message = "closed connection (Reason={Reason})")]
        internal static partial void LogConnectionClosed(
            this ILogger logger,
            string reason);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectionConnectFailed,
            EventName = nameof(TransportEventIds.ConnectionConnectFailed),
            Level = LogLevel.Debug,
            Message = "connection establishment failed")]
        internal static partial void LogConnectionConnectFailed(this ILogger logger, Exception? exception);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectionEstablished,
            EventName = nameof(TransportEventIds.ConnectionEstablished),
            Level = LogLevel.Debug,
            Message = "established connection")]
        internal static partial void LogConnectionEstablished(this ILogger logger);

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
            EventId = (int)TransportEventIds.SocketNetworkConnectionEstablished,
            EventName = nameof(TransportEventIds.SocketNetworkConnectionEstablished),
            Level = LogLevel.Debug,
            Message = "established connection (ReceiveBufferSize={RcvSize}, SendBufferSize={SndSize})")]
        internal static partial void LogSocketNetworkConnectionEstablished(this ILogger logger, int rcvSize, int sndSize);

        [LoggerMessage(
            EventId = (int)TransportEventIds.ConnectionEventHandlerException,
            EventName = nameof(TransportEventIds.ConnectionEventHandlerException),
            Level = LogLevel.Warning,
            Message = "{Name} event handler raised exception")]
        internal static partial void LogConnectionEventHandlerException(this ILogger logger, string name, Exception ex);

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
            EventId = (int)TransportEventIds.SentData,
            EventName = nameof(TransportEventIds.SentData),
            Level = LogLevel.Trace,
            Message = "sent {Size} bytes ({Data})")]
        internal static partial void LogSentData(this ILogger logger, int size, string data);

        [LoggerMessage(
            EventId = (int)TransportEventIds.StartReceivingDatagrams,
            EventName = nameof(TransportEventIds.StartReceivingDatagrams),
            Level = LogLevel.Information,
            Message = "starting to receive datagrams")]
        internal static partial void LogStartReceivingDatagrams(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEventIds.SocketStartReceivingDatagrams,
            EventName = nameof(TransportEventIds.SocketStartReceivingDatagrams),
            Level = LogLevel.Information,
            Message = "starting to receive datagrams (ReceiveBufferSize={RcvSize}, SendBufferSize={SndSize}")]
        internal static partial void LogSocketStartReceivingDatagrams(this ILogger logger, int rcvSize, int sndSize);

        [LoggerMessage(
            EventId = (int)TransportEventIds.StartReceivingDatagramsFailed,
            EventName = nameof(TransportEventIds.StartReceivingDatagramsFailed),
            Level = LogLevel.Information,
            Message = "starting receiving datagrams failed")]
        internal static partial void LogStartReceivingDatagramsFailed(this ILogger logger, Exception? exception);

        [LoggerMessage(
            EventId = (int)TransportEventIds.StartSendingDatagrams,
            EventName = nameof(TransportEventIds.StartSendingDatagrams),
            Level = LogLevel.Debug,
            Message = "starting to send datagrams")]
        internal static partial void LogStartSendingDatagrams(this ILogger logger);

        [LoggerMessage(
            EventId = (int)TransportEventIds.SocketStartSendingDatagrams,
            EventName = nameof(TransportEventIds.SocketStartSendingDatagrams),
            Level = LogLevel.Debug,
            Message = "starting to send datagrams (ReceiveBufferSize={RcvSize}, SendBufferSize={SndSize}")]
        internal static partial void LogSocketStartSendingDatagrams(this ILogger logger, int rcvSize, int sndSize);

        [LoggerMessage(
            EventId = (int)TransportEventIds.StartSendingDatagramsFailed,
            EventName = nameof(TransportEventIds.StartSendingDatagramsFailed),
            Level = LogLevel.Debug,
            Message = "starting sending datagrams failed")]
        internal static partial void LogStartSendingDatagramsFailed(this ILogger logger, Exception? exception);

        [LoggerMessage(
            EventId = (int)TransportEventIds.StopReceivingDatagrams,
            EventName = nameof(TransportEventIds.StopReceivingDatagrams),
            Level = LogLevel.Information,
            Message = "stopping to receive datagrams")]
        internal static partial void LogStopReceivingDatagrams(this ILogger logger);

        internal static IDisposable? StartListenerScope(this ILogger logger, IListener listener) =>
            logger.IsEnabled(LogLevel.Error) ? _listenerScope(logger, listener.Endpoint.ToString()) : null;

        internal static IDisposable? StartConnectionScope(
            this ILogger logger,
            NetworkConnectionInformation information,
            bool isServer) =>
            _connectionScope(
                logger,
                isServer,
                information.LocalEndpoint.ToString(),
                information.RemoteEndpoint.ToString());

        internal static IDisposable? StartConnectionScope(
            this ILogger logger,
            Endpoint endpoint,
            bool isServer) =>
            _connectionScope(
                logger,
                isServer,
                isServer ? endpoint.ToString() : "undefined",
                isServer ? "undefined" : endpoint.ToString());

        internal static IDisposable? StartStreamScope(this ILogger logger, long id) =>
            (id % 4) switch
            {
                0 => _streamScope(logger, id, "Client", "Bidirectional"),
                1 => _streamScope(logger, id, "Server", "Bidirectional"),
                2 => _streamScope(logger, id, "Client", "Unidirectional"),
                _ => _streamScope(logger, id, "Server", "Unidirectional")
            };
    }
}
