// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>This class contains ILogger extension methods for logging calls to the protocol connection APIs.</summary>
internal static partial class ProtocolLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)ProtocolEventId.ConnectionAccepted,
        EventName = nameof(ProtocolEventId.ConnectionAccepted),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' accepted connection from '{RemoteNetworkAddress}'")]
    internal static partial void LogConnectionAccepted(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ProtocolEventId.ConnectionAcceptFailed,
        EventName = nameof(ProtocolEventId.ConnectionAcceptFailed),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' failed to accept a new connection")]
    internal static partial void LogConnectionAcceptFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventId.ConnectionConnected,
        EventName = nameof(ProtocolEventId.ConnectionConnected),
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
            isServer ? "Server|Client" : "Client|Server",
            localNetworkAddress,
            remoteNetworkAddress);

    // Multiple logging methods are using same event id.
#pragma warning disable SYSLIB1006
    [LoggerMessage(
        EventId = (int)ProtocolEventId.ConnectionConnectFailed,
        EventName = nameof(ProtocolEventId.ConnectionConnectFailed),
        Level = LogLevel.Trace,
        Message = "Client|Server connection connect to '{ServerAddress}' failed")]
    internal static partial void LogConnectionConnectFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventId.ConnectionConnectFailed,
        EventName = nameof(ProtocolEventId.ConnectionConnectFailed),
        Level = LogLevel.Trace,
        Message = "Server|Client connection connect from '{ServerAddress}' to '{RemoteNetworkAddress}' failed")]
    internal static partial void LogConnectionConnectFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress,
        Exception exception);
#pragma warning restore SYSLIB1006

    [LoggerMessage(
        EventId = (int)ProtocolEventId.ConnectionFailed,
        EventName = nameof(ProtocolEventId.ConnectionFailed),
        Level = LogLevel.Trace,
        Message = "{Kind} connection from '{LocalNetworkAddress}' to '{RemoteNetworkAddress}' failed")]
    internal static partial void LogConnectionFailed(
        this ILogger logger,
        string kind,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress,
        Exception exception);

    internal static void LogConnectionFailed(
        this ILogger logger,
        bool isServer,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress,
        Exception exception) =>
        LogConnectionFailed(
            logger,
            isServer ? "Server|Client" : "Client|Server",
            localNetworkAddress,
            remoteNetworkAddress,
            exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventId.ConnectionShutdown,
        EventName = nameof(ProtocolEventId.ConnectionShutdown),
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
            isServer ? "Server|Client" : "Client|Server",
            localNetworkAddress,
            remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ProtocolEventId.StartAcceptingConnections,
        EventName = nameof(ProtocolEventId.StartAcceptingConnections),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' start accepting connections")]
    internal static partial void LogStartAcceptingConnections(this ILogger logger, ServerAddress serverAddress);

    [LoggerMessage(
        EventId = (int)ProtocolEventId.StopAcceptingConnections,
        EventName = nameof(ProtocolEventId.StopAcceptingConnections),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' stop accepting connections")]
    internal static partial void LogStopAcceptingConnections(this ILogger logger, ServerAddress serverAddress);
}
