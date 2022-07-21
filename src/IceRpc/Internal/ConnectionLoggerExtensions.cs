// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>This class provides ILogger extension methods for connection-related messages.</summary>
internal static partial class ConnectionLoggerExtensions
{
    private static readonly Func<ILogger, EndPoint?, EndPoint?, IDisposable> _clientConnectionScope =
        LoggerMessage.DefineScope<EndPoint?, EndPoint?>(
            "ClientConnection(LocalNetworkAddress={LocalNetworkAddress}, RemoteNetworkAddress={RemoteNetworkAddress})");

    private static readonly Func<ILogger, Endpoint, IDisposable> _newClientConnectionScope =
        LoggerMessage.DefineScope<Endpoint>(
            "NewClientConnection(Endpoint={Endpoint})");

    private static readonly Func<ILogger, Endpoint, IDisposable> _newServerConnectionScope =
        LoggerMessage.DefineScope<Endpoint>(
            "NewServerConnection(Endpoint={Endpoint})");

    private static readonly Func<ILogger, string, string, IDisposable> _sendRequestScope =
        LoggerMessage.DefineScope<string, string>("SendRequest(Path={Path}, Operation={Operation})");

    private static readonly Func<ILogger, EndPoint?, EndPoint?, IDisposable> _serverConnectionScope =
        LoggerMessage.DefineScope<EndPoint?, EndPoint?>(
            "ServerConnection(LocalNetworkAddress={LocalNetworkAddress}, RemoteNetworkAddress={RemoteNetworkAddress})");

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.ConnectionClosedReason,
        EventName = nameof(ConnectionEventIds.ConnectionClosedReason),
        Level = LogLevel.Information,
        Message = "connection closed due to exception")]
    internal static partial void LogConnectionClosedReason(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.ConnectionShutdownReason,
        EventName = nameof(ConnectionEventIds.ConnectionShutdownReason),
        Level = LogLevel.Information,
        Message = "connection shutdown (Message={message})")]
    internal static partial void LogConnectionShutdownReason(this ILogger logger, string message);

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
        TransportConnectionInformation information) =>
        _clientConnectionScope(logger, information.LocalNetworkAddress, information.RemoteNetworkAddress);

    /// <summary>Starts a client or server connection scope.</summary>
    internal static IDisposable StartConnectionScope(
        this ILogger logger,
        TransportConnectionInformation information,
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
        TransportConnectionInformation information) =>
        _serverConnectionScope(logger, information.LocalNetworkAddress, information.RemoteNetworkAddress);

    /// <summary>Starts a scope for method IProtocolConnection.InvokeAsync.</summary>
    internal static IDisposable StartSendRequestScope(this ILogger logger, OutgoingRequest request) =>
        _sendRequestScope(logger, request.ServiceAddress.Path, request.Operation);
}
