// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;

namespace IceRpc.Internal
{
    /// <summary>This class contains the ILogger extension methods for connection-related messages.</summary>
    internal static partial class ConnectionLoggerExtensions
    {
        private static readonly Func<ILogger, bool, string, string, IDisposable> _connectionScope =
            LoggerMessage.DefineScope<bool, string, string>(
                "connection(IsServer={IsServer}, LocalEndpoint={LocalEndpoint}, RemoteEndpoint={RemoteEndpoint})");

        private static readonly Func<ILogger, string, string, IDisposable> _receiveResponseScope =
            LoggerMessage.DefineScope<string, string>("ReceiveResponse(Path={Path}, Operation={Operation})");

        private static readonly Func<ILogger, string, string, IDisposable> _sendRequestScope =
            LoggerMessage.DefineScope<string, string>("SendRequest(Path={Path}, Operation={Operation})");

        private static readonly Func<ILogger, string, string, ResultType, IDisposable> _sendResponseScope =
            LoggerMessage.DefineScope<string, string, ResultType>(
                "SendResponse(Path={Path}, Operation={Operation}, ResultType={ResultType})");

        [LoggerMessage(
            EventId = (int)ConnectionEventIds.ConnectionClosedReason,
            EventName = nameof(ConnectionEventIds.ConnectionClosedReason),
            Level = LogLevel.Debug,
            Message = "connection closed due to exception")]
        internal static partial void LogConnectionClosedReason(this ILogger logger, Exception exception);

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

        internal static IDisposable StartConnectionScope(
            this ILogger logger,
            NetworkConnectionInformation information,
            bool isServer) =>
            _connectionScope(
                logger,
                isServer,
                information.LocalEndpoint.ToString(),
                information.RemoteEndpoint.ToString());

        internal static IDisposable StartConnectionScope(
            this ILogger logger,
            Endpoint endpoint,
            bool isServer) =>
            _connectionScope(
                logger,
                isServer,
                isServer ? endpoint.ToString() : "undefined",
                isServer ? "undefined" : endpoint.ToString());

        /// <summary>Starts a scope for an incoming response.</summary>
        internal static IDisposable StartReceiveResponseScope(this ILogger logger, OutgoingRequest request) =>
            _receiveResponseScope(logger, request.Path, request.Operation);

        /// <summary>Starts a scope for an outgoing request.</summary>
        internal static IDisposable StartSendRequestScope(this ILogger logger, OutgoingRequest request) =>
            _sendRequestScope(logger, request.Path, request.Operation);

        /// <summary>Starts a scope for an outgoing response.</summary>
        internal static IDisposable StartSendResponseScope(
            this ILogger logger,
            OutgoingResponse response,
            IncomingRequest request) =>
            _sendResponseScope(logger, request.Path, request.Operation, response.ResultType);
    }
}
