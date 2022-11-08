// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>This class contains ILogger extension methods for logging calls to the protocol connection APIs.</summary>
internal static partial class ProtocolLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)ProtocolEventIds.AcceptConnectionSucceed,
        EventName = nameof(ProtocolEventIds.AcceptConnectionSucceed),
        Level = LogLevel.Trace,
        Message = "Listener {ServerAddress} accepted connection from {RemoteNetworkAddress}",
        SkipEnabledCheck = true)]
    internal static partial void AcceptConnectionSucceed(
        this ILogger logger,
        string serverAddress,
        string remoteNetworkAddress);

    internal static void AcceptConnectionSucceed(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            AcceptConnectionSucceed(
                logger,
                serverAddress.ToString(),
                remoteNetworkAddress.ToString() ?? "<unavailable>");
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.AcceptConnectionFailed,
        EventName = nameof(ProtocolEventIds.AcceptConnectionFailed),
        Level = LogLevel.Trace,
        Message = "Listener {ServerAddress} failed to accept a new connection")]
    internal static partial void ConnectionAcceptFailure(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionFailure,
        EventName = nameof(ProtocolEventIds.ConnectionFailure),
        Level = LogLevel.Trace,
        Message = "{Kind} connection {LocalNetworkAddress} -> {RemoteNetworkAddress} failure",
        SkipEnabledCheck = true)]
    internal static partial void ConnectionFailure(
        this ILogger logger,
        string kind,
        string localNetworkAddress,
        string remoteNetworkAddress,
        Exception exception);

    internal static void ConnectionFailure(
        this ILogger logger,
        bool isServer,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress,
        Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ConnectionFailure(
                logger,
                isServer ? "Server" : "Client",
                localNetworkAddress.ToString() ?? "<not-available>",
                remoteNetworkAddress.ToString() ?? "<not-available>",
                exception);
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionShutdown,
        EventName = nameof(ProtocolEventIds.ConnectionShutdown),
        Level = LogLevel.Trace,
        Message = "{Kind} connection {LocalNetworkAddress} -> {RemoteNetworkAddress} shutdown",
        SkipEnabledCheck = true)]
    internal static partial void ConnectionShutdown(
        this ILogger logger,
        string kind,
        string localNetworkAddress,
        string remoteNetworkAddress);

    internal static void ConnectionShutdown(
        this ILogger logger,
        bool isServer,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ConnectionShutdown(
                logger,
                isServer ? "Server" : "Client",
                localNetworkAddress.ToString() ?? "<not-available>",
                remoteNetworkAddress.ToString() ?? "<not-available>");
        }
    }

    // Multiple logging methods are using same event id, we have two ConnectFailed overloads
#pragma warning disable SYSLIB1006
    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectFailed,
        EventName = nameof(ProtocolEventIds.ConnectFailed),
        Level = LogLevel.Trace,
        Message = "Client connect to {ServerAddress} failed")]
    internal static partial void ConnectFailed(this ILogger logger, ServerAddress serverAddress, Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectFailed,
        EventName = nameof(ProtocolEventIds.ConnectFailed),
        Level = LogLevel.Trace,
        Message = "Server {ServerAddress} connect to {RemoteNetworkAdress} failed",
        SkipEnabledCheck = true)]
    internal static partial void ConnectFailed(
        this ILogger logger,
        string serverAddress,
        string remoteNetworkAdress,
        Exception exception);
#pragma warning restore SYSLIB1006

    internal static void ConnectFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress,
        Exception exception)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ConnectFailed(
                logger,
                serverAddress.ToString(),
                remoteNetworkAddress.ToString() ?? "<not-available>",
                exception);
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectSucceed,
        EventName = nameof(ProtocolEventIds.ConnectSucceed),
        Level = LogLevel.Trace,
        Message = "{Kind} connect from {LocalNetworkAddress} to {RemoteNetworkAddress} succeeded",
        SkipEnabledCheck = true)]
    internal static partial void ConnectSucceeded(
        this ILogger logger,
        string kind,
        string localNetworkAddress,
        string remoteNetworkAddress);

    internal static void ConnectSucceeded(
        this ILogger logger,
        bool isServer,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress)
    {
        if (logger.IsEnabled(LogLevel.Trace))
        {
            ConnectSucceeded(
                logger,
                isServer ? "Server" : "Client",
                localNetworkAddress.ToString() ?? "<not-available>",
                remoteNetworkAddress.ToString() ?? "<not-available>");
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.StartAcceptingConnections,
        EventName = nameof(ProtocolEventIds.StartAcceptingConnections),
        Level = LogLevel.Trace,
        Message = "Listener {ServerAddress} start accepting connections")]
    internal static partial void StartAcceptingConnections(this ILogger logger, ServerAddress serverAddress);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.StopAcceptingConnections,
        EventName = nameof(ProtocolEventIds.StopAcceptingConnections),
        Level = LogLevel.Trace,
        Message = "Listener {ServerAddress} stop accepting connections")]
    internal static partial void StopAcceptingConnections(this ILogger logger, ServerAddress serverAddress);
}
