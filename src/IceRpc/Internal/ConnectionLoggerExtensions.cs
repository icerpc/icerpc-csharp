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

    private static readonly Func<ILogger, EndPoint?, EndPoint?, IDisposable> _serverConnectionScope =
        LoggerMessage.DefineScope<EndPoint?, EndPoint?>(
            "ServerConnection(LocalNetworkAddress={LocalNetworkAddress}, RemoteNetworkAddress={RemoteNetworkAddress})");

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
}
