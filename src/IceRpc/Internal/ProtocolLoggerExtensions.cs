// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>This class contains ILogger extension methods for logging calls to the protocol connection APIs.</summary>
internal static partial class ProtocolLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionAccepted,
        EventName = nameof(ProtocolEventIds.ConnectionAccepted),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' accepted connection from '{RemoteNetworkAddress}'")]
    internal static partial void LogConnectionAccepted(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionAcceptFailed,
        EventName = nameof(ProtocolEventIds.ConnectionAcceptFailed),
        Level = LogLevel.Error,
        Message = "Listener '{ServerAddress}' failed to accept a new connection")]
    internal static partial void LogConnectionAcceptFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionAcceptFailedAndContinue,
        EventName = nameof(ProtocolEventIds.ConnectionAcceptFailedAndContinue),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' failed to accept a new connection but continues accepting connections")]
    internal static partial void LogConnectionAcceptFailedAndContinue(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionConnected,
        EventName = nameof(ProtocolEventIds.ConnectionConnected),
        Level = LogLevel.Trace,
        Message = "{Kind} connection from '{LocalNetworkAddress}' to '{RemoteNetworkAddress}' connected")]
    internal static partial void LogConnectionConnected(
        this ILogger logger,
        string kind,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress);

    internal static void LogConnectionConnected(
        this ILogger logger,
        bool isServer,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress) =>
        LogConnectionConnected(
            logger,
            isServer ? "Server" : "Client",
            localNetworkAddress,
            remoteNetworkAddress);

    // Multiple logging methods are using same event id.
#pragma warning disable SYSLIB1006
    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionConnectFailed,
        EventName = nameof(ProtocolEventIds.ConnectionConnectFailed),
        Level = LogLevel.Trace,
        Message = "Client connection failed to connect to '{ServerAddress}'")]
    internal static partial void LogConnectionConnectFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionConnectFailed,
        EventName = nameof(ProtocolEventIds.ConnectionConnectFailed),
        Level = LogLevel.Trace,
        Message = "Server connection failed to connect from '{ServerAddress}' to '{RemoteNetworkAddress}'")]
    internal static partial void LogConnectionConnectFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress,
        Exception exception);
#pragma warning restore SYSLIB1006

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionDisposed,
        EventName = nameof(ProtocolEventIds.ConnectionDisposed),
        Level = LogLevel.Trace,
        Message = "{Kind} connection from '{LocalNetworkAddress}' to '{RemoteNetworkAddress}' disposed")]
    internal static partial void LogConnectionDisposed(
        this ILogger logger,
        string kind,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress);

    internal static void LogConnectionDisposed(
        this ILogger logger,
        bool isServer,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress) =>
        LogConnectionDisposed(
            logger,
            isServer ? "Server" : "Client",
            localNetworkAddress,
            remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionShutdown,
        EventName = nameof(ProtocolEventIds.ConnectionShutdown),
        Level = LogLevel.Trace,
        Message = "{Kind} connection from '{LocalNetworkAddress}' to '{RemoteNetworkAddress}' shutdown")]
    internal static partial void LogConnectionShutdown(
        this ILogger logger,
        string kind,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress);

    internal static void LogConnectionShutdown(
        this ILogger logger,
        bool isServer,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress) =>
        LogConnectionShutdown(
            logger,
            isServer ? "Server" : "Client",
            localNetworkAddress,
            remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionShutdownFailed,
        EventName = nameof(ProtocolEventIds.ConnectionShutdownFailed),
        Level = LogLevel.Trace,
        Message = "{Kind} connection from '{LocalNetworkAddress}' to '{RemoteNetworkAddress}' failed to shutdown")]
    internal static partial void LogConnectionShutdownFailed(
        this ILogger logger,
        string kind,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress,
        Exception exception);

    internal static void LogConnectionShutdownFailed(
        this ILogger logger,
        bool isServer,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress,
        Exception exception) =>
        LogConnectionShutdownFailed(
            logger,
            isServer ? "Server" : "Client",
            localNetworkAddress,
            remoteNetworkAddress,
            exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.StartAcceptingConnections,
        EventName = nameof(ProtocolEventIds.StartAcceptingConnections),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' has started accepting connections")]
    internal static partial void LogStartAcceptingConnections(this ILogger logger, ServerAddress serverAddress);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.StopAcceptingConnections,
        EventName = nameof(ProtocolEventIds.StopAcceptingConnections),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' has stopped accepting connections")]
    internal static partial void LogStopAcceptingConnections(this ILogger logger, ServerAddress serverAddress);
}
