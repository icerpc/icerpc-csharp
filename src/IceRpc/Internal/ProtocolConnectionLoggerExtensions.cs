// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>This class provides ILogger extension methods for protocol connection APIs. They are all at Information
/// level or higher.</summary>
internal static partial class ProtocolConnectionLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)ConnectionEventIds.Connect,
        EventName = nameof(ConnectionEventIds.Connect),
        Level = LogLevel.Information,
        Message = "established {Protocol} connection for {Endpoint} over {LocalNetworkAddress}<->{RemoteNetworkAddress}")]
    internal static partial void LogProtocolConnectionConnect(
        this ILogger logger,
        Protocol protocol,
        Endpoint endpoint,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.ConnectException,
        EventName = nameof(ConnectionEventIds.ConnectException),
        Level = LogLevel.Information,
        Message = "failed to establish {Protocol} connection for {Endpoint}")]
    internal static partial void LogProtocolConnectionConnectException(
        this ILogger logger,
        Protocol protocol,
        Endpoint endpoint,
        Exception exception);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.Dispose,
        EventName = nameof(ConnectionEventIds.Dispose),
        Level = LogLevel.Information,
        Message = "disposed {Protocol} connection for {Endpoint} over {LocalNetworkAddress}<->{RemoteNetworkAddress}")]
    internal static partial void LogProtocolConnectionDispose(
        this ILogger logger,
        Protocol protocol,
        Endpoint endpoint,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.Shutdown,
        EventName = nameof(ConnectionEventIds.Shutdown),
        Level = LogLevel.Information,
        Message = "shut down {Protocol} connection for {Endpoint} over {LocalNetworkAddress}<->{RemoteNetworkAddress}: {Message}")]
    internal static partial void LogProtocolConnectionShutdown(
        this ILogger logger,
        Protocol protocol,
        Endpoint endpoint,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress,
        string message);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.ShutdownException,
        EventName = nameof(ConnectionEventIds.ShutdownException),
        Level = LogLevel.Information,
        Message = "failed to shut down {Protocol} connection for {Endpoint} over {LocalNetworkAddress}<->{RemoteNetworkAddress}")]
    internal static partial void LogProtocolConnectionShutdownException(
        this ILogger logger,
        Protocol protocol,
        Endpoint endpoint,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress,
        Exception exception);
}
