// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>Provides <see cref="ILogger" /> extension methods for logging related to <see cref="IProtocolConnection" />
/// and its implementations.</summary>
internal static partial class ProtocolLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionAccepted,
        EventName = nameof(ProtocolEventIds.ConnectionAccepted),
        Level = LogLevel.Debug,
        Message = "Listener '{ServerAddress}' accepted connection from '{RemoteNetworkAddress}'")]
    internal static partial void LogConnectionAccepted(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionAcceptFailed,
        EventName = nameof(ProtocolEventIds.ConnectionAcceptFailed),
        Level = LogLevel.Critical,
        Message = "Listener '{ServerAddress}' failed to accept a new connection and stopped accepting connections")]
    internal static partial void LogConnectionAcceptFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionAcceptFailedWithRetryableException,
        EventName = nameof(ProtocolEventIds.ConnectionAcceptFailedWithRetryableException),
        Level = LogLevel.Debug,
        Message = "Listener '{ServerAddress}' failed to accept a new connection but continues accepting connections")]
    internal static partial void LogConnectionAcceptFailedWithRetryableException(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionConnected,
        EventName = nameof(ProtocolEventIds.ConnectionConnected),
        Level = LogLevel.Debug,
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
        Level = LogLevel.Debug,
        Message = "Client connection failed to connect to '{ServerAddress}'")]
    internal static partial void LogConnectionConnectFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.ConnectionConnectFailed,
        EventName = nameof(ProtocolEventIds.ConnectionConnectFailed),
        Level = LogLevel.Debug,
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
        Level = LogLevel.Debug,
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
        Level = LogLevel.Debug,
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
        Level = LogLevel.Debug,
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
        EventId = (int)ProtocolEventIds.DispatchFailed,
        EventName = nameof(ProtocolEventIds.DispatchFailed),
        Message = "Dispatch of '{Operation}' on '{Path}' from '{RemoteNetworkAddress}' failed")]
    internal static partial void LogDispatchFailed(
        this ILogger logger,
        LogLevel logLevel,
        string operation,
        string path,
        EndPoint remoteNetworkAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.DispatchRefused,
        EventName = nameof(ProtocolEventIds.DispatchRefused),
        Message = "Refused dispatch from '{RemoteNetworkAddress}'")]
    internal static partial void LogDispatchRefused(
        this ILogger logger,
        LogLevel logLevel,
        EndPoint remoteNetworkAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.RequestPayloadContinuationFailed,
        EventName = nameof(ProtocolEventIds.RequestPayloadContinuationFailed),
        Message = "Invocation '{Operation}' on '{Path}' failed to send payload continuation to '{RemoteNetworkAddress}'")]
    internal static partial void LogRequestPayloadContinuationFailed(
        this ILogger logger,
        LogLevel logLevel,
        string operation,
        string path,
        EndPoint remoteNetworkAddress,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.StartAcceptingConnections,
        EventName = nameof(ProtocolEventIds.StartAcceptingConnections),
        Level = LogLevel.Debug,
        Message = "Listener '{ServerAddress}' has started accepting connections")]
    internal static partial void LogStartAcceptingConnections(this ILogger logger, ServerAddress serverAddress);

    [LoggerMessage(
        EventId = (int)ProtocolEventIds.StopAcceptingConnections,
        EventName = nameof(ProtocolEventIds.StopAcceptingConnections),
        Level = LogLevel.Debug,
        Message = "Listener '{ServerAddress}' has stopped accepting connections")]
    internal static partial void LogStopAcceptingConnections(this ILogger logger, ServerAddress serverAddress);
}
