// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>This class provides ILogger extension methods for connection events.</summary>
internal static partial class ConnectionLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)ConnectionEventId.Connect,
        EventName = nameof(ConnectionEventId.Connect),
        Level = LogLevel.Debug,
        Message = "Connection for {ServerAddress} established over {LocalNetworkAddress}<->{RemoteNetworkAddress}")]
    internal static partial void LogConnectionConnect(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ConnectionEventId.ConnectException,
        EventName = nameof(ConnectionEventId.ConnectException),
        Level = LogLevel.Debug,
        Message = "Connection for {ServerAddress} could not be established")]
    internal static partial void LogConnectionConnectException(
        this ILogger logger,
        Exception exception,
        ServerAddress serverAddress);

    [LoggerMessage(
        EventId = (int)ConnectionEventId.Dispose,
        EventName = nameof(ConnectionEventId.Dispose),
        Level = LogLevel.Debug,
        Message = "Connection for {ServerAddress} over {LocalNetworkAddress}<->{RemoteNetworkAddress} disposed")]
    internal static partial void LogConnectionDispose(
        this ILogger logger,
        ServerAddress serverAddress,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ConnectionEventId.Shutdown,
        EventName = nameof(ConnectionEventId.Shutdown),
        Level = LogLevel.Debug,
        Message = "Connection for {ServerAddress} over {LocalNetworkAddress}<->{RemoteNetworkAddress} shut down " +
            "successfully: {Message}")]
    internal static partial void LogConnectionShutdown(
        this ILogger logger,
        string message,
        ServerAddress serverAddress,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ConnectionEventId.ShutdownException,
        EventName = nameof(ConnectionEventId.ShutdownException),
        Level = LogLevel.Debug,
        Message = "Connection for {ServerAddress} over {LocalNetworkAddress}<->{RemoteNetworkAddress} failed to shut down")]
    internal static partial void LogConnectionShutdownException(
        this ILogger logger,
        Exception exception,
        ServerAddress serverAddress,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress);
}
