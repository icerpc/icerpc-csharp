// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
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
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' failed to accept a new connection")]
    internal static partial void LogConnectionAcceptFailed(
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
            isServer ? "Server|Client" : "Client|Server",
            localNetworkAddress,
            remoteNetworkAddress);

    // Multiple logging methods are using same event id.
#pragma warning disable SYSLIB1006
    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionConnectFailed,
        EventName = nameof(ProtocolEventIds.ConnectionConnectFailed),
        Level = LogLevel.Trace,
        Message = "Client|Server connection connect to '{ServerAddress}' failed")]
    internal static partial void LogConnectionConnectFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionConnectFailed,
        EventName = nameof(ProtocolEventIds.ConnectionConnectFailed),
        Level = LogLevel.Trace,
        Message = "Server|Client connection connect from '{ServerAddress}' to '{RemoteNetworkAddress}' failed")]
    internal static partial void LogConnectionConnectFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress,
        Exception exception);
#pragma warning restore SYSLIB1006

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionFailed,
        EventName = nameof(ProtocolEventIds.ConnectionFailed),
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
            isServer ? "Server|Client" : "Client|Server",
            localNetworkAddress,
            remoteNetworkAddress);

    // Multiple logging methods are using same event id.
#pragma warning disable SYSLIB1006
    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionDispatchFailed,
        EventName = nameof(ProtocolEventIds.ConnectionDispatchFailed),
        Level = LogLevel.Error,
        Message = "Request dispatch failed")]
    internal static partial void LogConnectionDispatchFailed(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionDispatchFailed,
        EventName = nameof(ProtocolEventIds.ConnectionDispatchFailed),
        Level = LogLevel.Error,
        Message = "Request dispatch '{Path}/{Operation}' failed")]
    internal static partial void LogConnectionDispatchFailed(
        this ILogger logger,
        string path,
        string operation,
        Exception exception);
#pragma warning restore SYSLIB1006

    internal static void LogConnectionDispatchFailed(
        this ILogger logger,
        IncomingRequest? request,
        Exception exception)
    {
        if (request is not null)
        {
            LogConnectionDispatchFailed(logger, request.Path, request.Operation, exception);
        }
        else
        {
            LogConnectionDispatchFailed(logger, exception);
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.StartAcceptingConnections,
        EventName = nameof(ProtocolEventIds.StartAcceptingConnections),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' start accepting connections")]
    internal static partial void LogStartAcceptingConnections(this ILogger logger, ServerAddress serverAddress);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.StopAcceptingConnections,
        EventName = nameof(ProtocolEventIds.StopAcceptingConnections),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' stop accepting connections")]
    internal static partial void LogStopAcceptingConnections(this ILogger logger, ServerAddress serverAddress);
}
