// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Internal;

/// <summary>This class provides ILogger extension methods for connection-related messages.</summary>
internal static partial class ProtocolConnectionLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)ConnectionEventIds.ConnectionConnect,
        EventName = nameof(ConnectionEventIds.ConnectionConnect),
        Level = LogLevel.Information,
        Message = "established {Protocol} connection for {Endpoint} over {LocalNetworkAddress}<->{RemoteNetworkAddress}")]
    internal static partial void LogProtocolConnectionConnect(
        this ILogger logger,
        Protocol protocol,
        Endpoint endpoint,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)ConnectionEventIds.ConnectionConnectException,
        EventName = nameof(ConnectionEventIds.ConnectionConnectException),
        Level = LogLevel.Information,
        Message = "failed to establish {Protocol} connection for {Endpoint}")]
    internal static partial void LogProtocolConnectionConnectException(
        this ILogger logger,
        Protocol protocol,
        Endpoint endpoint,
        Exception exception);
}
