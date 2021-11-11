// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>This class provides ILogger extension methods for connection-related messages.</summary>
    internal static partial class ConnectionLoggerExtensions
    {
        private static readonly Func<ILogger, Endpoint, Endpoint, IDisposable> _clientConnectionScope =
            LoggerMessage.DefineScope<Endpoint, Endpoint>(
                "ClientConnection(LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint})");

        private static readonly Func<ILogger, Endpoint, IDisposable> _newClientConnectionScope =
            LoggerMessage.DefineScope<Endpoint>(
                "NewClientConnection(RemoteEndpoint={RemoteEndpoint})");

        private static readonly Func<ILogger, Endpoint, IDisposable> _newServerConnectionScope =
            LoggerMessage.DefineScope<Endpoint>(
                "NewServerConnection(LocalEndpoint={LocalEndpoint})");

        private static readonly Func<ILogger, string, string, IDisposable> _receiveResponseScope =
            LoggerMessage.DefineScope<string, string>("ReceiveResponse(Path={Path}, Operation={Operation})");

        private static readonly Func<ILogger, string, string, IDisposable> _sendRequestScope =
            LoggerMessage.DefineScope<string, string>("SendRequest(Path={Path}, Operation={Operation})");

        private static readonly Func<ILogger, string, string, ResultType, IDisposable> _sendResponseScope =
            LoggerMessage.DefineScope<string, string, ResultType>(
                "SendResponse(Path={Path}, Operation={Operation}, ResultType={ResultType})");

        private static readonly Func<ILogger, Endpoint, Endpoint, IDisposable> _serverConnectionScope =
            LoggerMessage.DefineScope<Endpoint, Endpoint>(
                "ServerConnection(LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint})");

        [LoggerMessage(
            EventId = (int)ConnectionEventIds.ConnectionClosedReason,
            EventName = nameof(ConnectionEventIds.ConnectionClosedReason),
            Level = LogLevel.Debug,
            Message = "connection closed due to exception")]
        internal static partial void LogConnectionClosedReason(this ILogger logger, Exception exception);

        [LoggerMessage(
            EventId = (int)ConnectionEventIds.CreateProtocolConnection,
            EventName = nameof(ConnectionEventIds.CreateProtocolConnection),
            Level = LogLevel.Information,
            Message = "{Protocol} connection established: " +
                "LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint}")]
        internal static partial void LogCreateProtocolConnection(
            this ILogger logger,
            Protocol protocol,
            Endpoint localEndpoint,
            Endpoint remoteEndpoint);

        [LoggerMessage(
            EventId = (int)ConnectionEventIds.Ping,
            EventName = nameof(ConnectionEventIds.Ping),
            Level = LogLevel.Debug,
            Message = "sent ping")]
        internal static partial void LogPing(this ILogger logger);

        [LoggerMessage(
            EventId = (int)ConnectionEventIds.ProtocolConnectionDispose,
            EventName = nameof(ConnectionEventIds.ProtocolConnectionDispose),
            Level = LogLevel.Information,
            Message = "{Protocol} connection disposed")]
        internal static partial void LogProtocolConnectionDispose(this ILogger logger, Protocol protocol);

        [LoggerMessage(
            EventId = (int)ConnectionEventIds.ProtocolConnectionShutdown,
            EventName = nameof(ConnectionEventIds.ProtocolConnectionShutdown),
            Level = LogLevel.Debug,
            Message = "{Protocol} connection shut down: {Message}")]
        internal static partial void LogProtocolConnectionShutdown(
            this ILogger logger,
            Protocol protocol,
            string message);

        [LoggerMessage(
            EventId = (int)ConnectionEventIds.ReceiveRequest,
            EventName = nameof(ConnectionEventIds.ReceiveRequest),
            Level = LogLevel.Debug,
            Message = "received request frame (Path={Path}, Operation={Operation}, PayloadSize={PayloadSize}, " +
                "PayloadEncoding={PayloadEncoding})")]
        internal static partial void LogReceiveRequest(
            this ILogger logger,
            string path,
            string operation,
            int payloadSize,
            Encoding payloadEncoding);

        [LoggerMessage(
            EventId = (int)ConnectionEventIds.ReceiveResponse,
            EventName = nameof(ConnectionEventIds.ReceiveResponse),
            Level = LogLevel.Debug,
            Message = "received response frame (PayloadSize={PayloadSize}, PayloadEncoding={PayloadEncoding}, " +
                "ResultType={ResultType})")]
        internal static partial void LogReceiveResponse(
            this ILogger logger,
            int payloadSize,
            Encoding payloadEncoding,
            ResultType resultType);

        [LoggerMessage(
            EventId = (int)ConnectionEventIds.SendRequest,
            EventName = nameof(ConnectionEventIds.SendRequest),
            Level = LogLevel.Debug,
            Message = "sent request frame (PayloadSize={PayloadSize}, PayloadEncoding={PayloadEncoding})")]
        internal static partial void LogSendRequest(
            this ILogger logger,
            int payloadSize,
            Encoding payloadEncoding);

        [LoggerMessage(
            EventId = (int)ConnectionEventIds.SendResponse,
            EventName = nameof(ConnectionEventIds.SendResponse),
            Level = LogLevel.Debug,
            Message = "sent response frame (PayloadSize={PayloadSize}, PayloadEncoding={PayloadEncoding})")]
        internal static partial void LogSendResponse(
            this ILogger logger,
            int payloadSize,
            Encoding payloadEncoding);

        /// <summary>Starts a client connection scope.</summary>
        internal static IDisposable StartClientConnectionScope(
            this ILogger logger,
            NetworkConnectionInformation information) =>
            _clientConnectionScope(logger, information.LocalEndpoint, information.RemoteEndpoint);

        /// <summary>Starts a client or server connection scope.</summary>
        internal static IDisposable StartConnectionScope(
            this ILogger logger,
            NetworkConnectionInformation information,
            bool isServer) =>
            isServer ? logger.StartServerConnectionScope(information) : logger.StartClientConnectionScope(information);

        /// <summary>Starts a client or server connection scope.</summary>
        internal static IDisposable StartNewConnectionScope(
            this ILogger logger,
            Endpoint endpoint,
            bool isServer) =>
            isServer ? _newServerConnectionScope(logger, endpoint) : _newClientConnectionScope(logger, endpoint);

        /// <summary>Starts a server connection scope.</summary>
        internal static IDisposable StartServerConnectionScope(
            this ILogger logger,
            NetworkConnectionInformation information) =>
            _serverConnectionScope(logger, information.LocalEndpoint, information.RemoteEndpoint);

        /// <summary>Starts a scope for method IProtocolConnection.ReceiveResponseAsync.</summary>
        internal static IDisposable StartReceiveResponseScope(this ILogger logger, OutgoingRequest request) =>
            _receiveResponseScope(logger, request.Path, request.Operation);

        /// <summary>Starts a scope for method IProtocolConnection.SendRequestAsync.</summary>
        internal static IDisposable StartSendRequestScope(this ILogger logger, OutgoingRequest request) =>
            _sendRequestScope(logger, request.Path, request.Operation);

        /// <summary>Starts a scope for method IProtocolConnection.SendResponseAsync.</summary>
        internal static IDisposable StartSendResponseScope(
            this ILogger logger,
            OutgoingResponse response,
            IncomingRequest request) =>
            _sendResponseScope(logger, request.Path, request.Operation, response.ResultType);
    }
}
