// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>This class provides ILogger extension methods for connection-related messages.</summary>
internal static partial class ConnectionLoggerExtensions
{
    private static readonly Func<ILogger, EndPoint, EndPoint, IDisposable> _clientConnectionScope =
        LoggerMessage.DefineScope<EndPoint, EndPoint>(
            "ClientConnection(LocalEndPoint={LocalEndPoint}, RemoteEndPoint={RemoteEndPoint})");

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

    private static readonly Func<ILogger, EndPoint, EndPoint, IDisposable> _serverConnectionScope =
        LoggerMessage.DefineScope<EndPoint, EndPoint>(
            "ServerConnection(LocalEndPoint={LocalEndPoint}, RemoteEndPoint={RemoteEndPoint})");

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.AcceptRequests,
        EventName = nameof(ConnectionEventIds.AcceptRequests),
        Level = LogLevel.Debug,
        Message = "accepting request frames")]
    internal static partial void LogAcceptRequests(this ILogger logger);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.ConnectionClosedReason,
        EventName = nameof(ConnectionEventIds.ConnectionClosedReason),
        Level = LogLevel.Information,
        Message = "connection closed due to exception")]
    internal static partial void LogConnectionClosedReason(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.ProtocolConnectionConnect,
        EventName = nameof(ConnectionEventIds.ProtocolConnectionConnect),
        Level = LogLevel.Information,
        Message = "{Protocol} connection established " +
            "(LocalEndPoint={LocalEndPoint}, RemoteEndPoint={RemoteEndPoint})")]
    internal static partial void LogProtocolConnectionConnect(
        this ILogger logger,
        Protocol protocol,
        EndPoint localEndPoint,
        EndPoint remoteEndPoint);

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
        Message = "{Protocol} connection shut down (Message={Message})")]
    internal static partial void LogProtocolConnectionShutdown(
        this ILogger logger,
        Protocol protocol,
        string message);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.ProtocolConnectionShutdownCanceled,
        EventName = nameof(ConnectionEventIds.ProtocolConnectionShutdownCanceled),
        Level = LogLevel.Debug,
        Message = "{Protocol} connection shut down canceled")]
    internal static partial void LogProtocolConnectionShutdownCanceled(this ILogger logger, Protocol protocol);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.SendRequest,
        EventName = nameof(ConnectionEventIds.SendRequest),
        Level = LogLevel.Debug,
        Message = "sent request frame")]
    internal static partial void LogSendRequest(this ILogger logger);

    /// <summary>Starts a client connection scope.</summary>
    internal static IDisposable StartClientConnectionScope(
        this ILogger logger,
        NetworkConnectionInformation information) =>
        _clientConnectionScope(logger, information.LocalEndPoint, information.RemoteEndPoint);

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
        _serverConnectionScope(logger, information.LocalEndPoint, information.RemoteEndPoint);

    /// <summary>Starts a scope for method IProtocolConnection.ReceiveResponseAsync.</summary>
    internal static IDisposable StartReceiveResponseScope(this ILogger logger, OutgoingRequest request) =>
        _receiveResponseScope(logger, request.ServiceAddress.Path, request.Operation);

    /// <summary>Starts a scope for method IProtocolConnection.InvokeAsync.</summary>
    internal static IDisposable StartSendRequestScope(this ILogger logger, OutgoingRequest request) =>
        _sendRequestScope(logger, request.ServiceAddress.Path, request.Operation);

    /// <summary>Starts a scope for method IProtocolConnection.SendResponseAsync.</summary>
    internal static IDisposable StartSendResponseScope(
        this ILogger logger,
        OutgoingResponse response,
        IncomingRequest request) =>
        _sendResponseScope(logger, request.Path, request.Operation, response.ResultType);
}
