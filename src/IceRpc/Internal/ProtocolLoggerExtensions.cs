// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>This class contains ILogger extension methods for logging calls to the protocol connection APIs.</summary>
internal static partial class ProtocolLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)ProtocolEventIds.AcceptConnection,
        EventName = nameof(ProtocolEventIds.AcceptConnection),
        Level = LogLevel.Trace,
        Message = "Listener {ServerAddress} accepted connection from {RemoteNetworkAddress}",
        SkipEnabledCheck = true)]
    internal static partial void ConnectionAccepted(
        this ILogger logger,
        string serverAddress,
        string remoteNetworkAddress);

    internal static void ConnectionAccepted(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ConnectionAccepted(logger, serverAddress.ToString(), remoteNetworkAddress.ToString() ?? "<unavailable>");
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.AcceptConnectionFailed,
        EventName = nameof(ProtocolEventIds.AcceptConnectionFailed),
        Level = LogLevel.Trace,
        Message = "Listener {ServerAddress} failed to accept a new connection",
        SkipEnabledCheck = true)]
    internal static partial void ConnectionAcceptFailure(
        this ILogger logger,
        string serverAddress,
        Exception exception);

    internal static void ConnectionAcceptFailure(this ILogger logger, ServerAddress serverAddress, Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ConnectionAcceptFailure(logger, serverAddress.ToString(), exception);
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ClientConnectionFailure,
        EventName = nameof(ProtocolEventIds.ClientConnectionFailure),
        Level = LogLevel.Trace,
        Message = "Client connection {LocalNetworkAddress} -> {ServerAddress} failure",
        SkipEnabledCheck = true)]
    internal static partial void ClientConnectionFailure(
        this ILogger logger,
        string serverAddress,
        string localNetworkAddress,
        Exception exception);

    internal static void ClientConnectionFailure(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint localNetworkAddress,
        Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ClientConnectionFailure(
                logger,
                serverAddress.ToString(),
                localNetworkAddress.ToString() ?? "<not-available>",
                exception);
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ClientConnectionShutdown,
        EventName = nameof(ProtocolEventIds.ClientConnectionShutdown),
        Level = LogLevel.Trace,
        Message = "Client connection {LocalNetworkAddress} -> {ServerAddress} shutdown",
        SkipEnabledCheck = true)]
    internal static partial void ClientConnectionShutdown(
        this ILogger logger,
        string serverAddress,
        string localNetworkAddress);

    internal static void ClientConnectionShutdown(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint localNetworkAddress)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ClientConnectionShutdown(
                logger,
                serverAddress.ToString(),
                localNetworkAddress.ToString() ?? "<not-available>");
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ClientConnectFailed,
        EventName = nameof(ProtocolEventIds.ClientConnectFailed),
        Level = LogLevel.Trace,
        Message = "Client connect to {ServerAddress} failed",
        SkipEnabledCheck = true)]
    internal static partial void ClientConnectFailed(this ILogger logger, string serverAddress, Exception exception);

    internal static void ClientConnectFailed(this ILogger logger, ServerAddress serverAddress, Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ClientConnectFailed(logger, serverAddress.ToString(), exception);
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ClientConnectSucceed,
        EventName = nameof(ProtocolEventIds.ClientConnectSucceed),
        Level = LogLevel.Trace,
        Message = "Client connect to {ServerAddress},  {LocalNetworkAddress} -> {RemoteNetworkAddress} succeed",
        SkipEnabledCheck = true)]
    internal static partial void ClientConnectSucceed(
        this ILogger logger,
        string serverAddress,
        string localNetworkAddress,
        string remoteNetworkAddress);

    internal static void ClientConnectSucceed(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ClientConnectSucceed(
                logger,
                serverAddress.ToString(),
                localNetworkAddress.ToString() ?? "<not-available>",
                remoteNetworkAddress.ToString() ?? "<not-available>");
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ServerConnectionFailure,
        EventName = nameof(ProtocolEventIds.ServerConnectionFailure),
        Level = LogLevel.Trace,
        Message = "Server connection {ServerAddress} -> {RemoteNetworkAddress} failure",
        SkipEnabledCheck = true)]
    internal static partial void ServerConnectionFailure(
        this ILogger logger,
        string serverAddress,
        string remoteNetworkAddress,
        Exception exception);

    internal static void ServerConnectionFailure(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress,
        Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ServerConnectionFailure(
                logger,
                serverAddress.ToString(),
                remoteNetworkAddress.ToString() ?? "<not-available>",
                exception);
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ServerConnectionShutdown,
        EventName = nameof(ProtocolEventIds.ServerConnectionShutdown),
        Level = LogLevel.Trace,
        Message = "Server connection {ServerAddress} -> {RemoteNetworkAddress} shutdown",
        SkipEnabledCheck = true)]
    internal static partial void ServerConnectionShutdown(
        this ILogger logger,
        string serverAddress,
        string remoteNetworkAddress);

    internal static void ServerConnectionShutdown(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ServerConnectionShutdown(
                logger,
                serverAddress.ToString(),
                remoteNetworkAddress.ToString() ?? "<not-available>");
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ServerConnectFailed,
        EventName = nameof(ProtocolEventIds.ServerConnectFailed),
        Level = LogLevel.Trace,
        Message = "Server {ServerAddress} connect to {RemoteNetworkAddress} failed",
        SkipEnabledCheck = true)]
    internal static partial void ServerConnectFailed(
        this ILogger logger,
        string serverAddress,
        string remoteNetworkAddress,
        Exception exception);

    internal static void ServerConnectFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress,
        Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ServerConnectFailed(
                logger,
                serverAddress.ToString(),
                remoteNetworkAddress.ToString() ?? "<not-available>",
                exception);
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ServerConnectSucceed,
        EventName = nameof(ProtocolEventIds.ServerConnectSucceed),
        Level = LogLevel.Trace,
        Message = "Server {ServerAddress} connected to {RemoteNetworkAddress} succeed",
        SkipEnabledCheck = true)]
    internal static partial void ServerConnectSucceed(
        this ILogger logger,
        string serverAddress,
        string remoteNetworkAddress);

    internal static void ServerConnectSucceed(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ServerConnectSucceed(logger, serverAddress.ToString(), remoteNetworkAddress.ToString() ?? "<not-available>");
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.StartAcceptingConnections,
        EventName = nameof(ProtocolEventIds.StartAcceptingConnections),
        Level = LogLevel.Trace,
        Message = "Listener {ServerAddress} start accepting connections",
        SkipEnabledCheck = true)]
    internal static partial void StartAcceptingConnections(this ILogger logger, string serverAddress);

    internal static void StartAcceptingConnections(this ILogger logger, ServerAddress serverAddress)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            StartAcceptingConnections(logger, serverAddress.ToString());
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.StopAcceptingConnections,
        EventName = nameof(ProtocolEventIds.StopAcceptingConnections),
        Level = LogLevel.Trace,
        Message = "Listener {ServerAddress} stop accepting connections",
        SkipEnabledCheck = true)]
    internal static partial void StopAcceptingConnections(this ILogger logger, string serverAddress);

    internal static void StopAcceptingConnections(this ILogger logger, ServerAddress serverAddress)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            StopAcceptingConnections(logger, serverAddress.ToString());
        }
    }
}
