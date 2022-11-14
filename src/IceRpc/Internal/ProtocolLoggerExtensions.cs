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
    internal static partial void ConnectionAccepted(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionAcceptFailed,
        EventName = nameof(ProtocolEventIds.ConnectionAcceptFailed),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' failed to accept a new connection")]
    internal static partial void ConnectionAcceptFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionConnected,
        EventName = nameof(ProtocolEventIds.ConnectionConnected),
        Level = LogLevel.Trace,
        Message = "{Kind} connection from '{LocalNetworkAddress}' to '{RemoteNetworkAddress}' connected")]
    internal static partial void ConnectionConnected(
        this ILogger logger,
        string kind,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress);

    internal static void ConnectionConnected(
        this ILogger logger,
        bool isServer,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress) =>
        ConnectionConnected(
            logger,
            isServer ? "Server|Client" : "Client|Server",
            localNetworkAddress,
            remoteNetworkAddress);

    // Multiple logging methods are using same event id, we have two ConnectFailed overloads
#pragma warning disable SYSLIB1006
    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionConnectFailed,
        EventName = nameof(ProtocolEventIds.ConnectionConnectFailed),
        Level = LogLevel.Trace,
        Message = "Client|Server connection connect to '{ServerAddress}' failed")]
    internal static partial void ConnectionConnectFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionConnectFailed,
        EventName = nameof(ProtocolEventIds.ConnectionConnectFailed),
        Level = LogLevel.Trace,
        Message = "Server|Client connection connect from '{ServerAddress}' to '{RemoteNetworkAdress}' failed")]
    internal static partial void ConnectionConnectFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAdress,
        Exception exception);
#pragma warning restore SYSLIB1006

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionFailed,
        EventName = nameof(ProtocolEventIds.ConnectionFailed),
        Level = LogLevel.Trace,
        Message = "{Kind} connection from '{LocalNetworkAddress}' to '{RemoteNetworkAddress}' failed")]
    internal static partial void ConnectionFailed(
        this ILogger logger,
        string kind,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress,
        Exception exception);

    internal static void ConnectionFailed(
        this ILogger logger,
        bool isServer,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress,
        Exception exception) =>
        ConnectionFailed(
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
    internal static partial void ConnectionShutdown(
        this ILogger logger,
        string kind,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress);

    internal static void ConnectionShutdown(
        this ILogger logger,
        bool isServer,
        EndPoint localNetworkAddress,
        EndPoint remoteNetworkAddress) =>
        ConnectionShutdown(
            logger,
            isServer ? "Server|Client" : "Client|Server",
            localNetworkAddress,
            remoteNetworkAddress);

#pragma warning disable SYSLIB1006
    [LoggerMessage(
        EventId = (int)ProtocolEventIds.UnhandledException,
        EventName = nameof(ProtocolEventIds.UnhandledException),
        Level = LogLevel.Error,
        Message = "Request dispatch failed with unhandled exception")]
    internal static partial void DispatchUnhandledException(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.UnhandledException,
        EventName = nameof(ProtocolEventIds.UnhandledException),
        Level = LogLevel.Error,
        Message = "Request dispatch '{Path}/{Operation}' failed with unhandled exception")]
    internal static partial void DispatchUnhandledException(
        this ILogger logger,
        string path,
        string operation,
        Exception exception);
#pragma warning restore SYSLIB1006

    internal static void DispatchUnhandledException(this ILogger logger, IncomingRequest? request, Exception exception)
    {
        if (request is not null)
        {
            logger.DispatchUnhandledException(request.Path, request.Operation, exception);
        }
        else
        {
            logger.DispatchUnhandledException(exception);
        }
    }

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.StartAcceptingConnections,
        EventName = nameof(ProtocolEventIds.StartAcceptingConnections),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' start accepting connections")]
    internal static partial void StartAcceptingConnections(this ILogger logger, ServerAddress serverAddress);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.StopAcceptingConnections,
        EventName = nameof(ProtocolEventIds.StopAcceptingConnections),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' stop accepting connections")]
    internal static partial void StopAcceptingConnections(this ILogger logger, ServerAddress serverAddress);
}
