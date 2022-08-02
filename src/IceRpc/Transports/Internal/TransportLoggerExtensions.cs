// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal;

/// <summary>This class contains ILogger extension methods for logging calls to the transport APIs.</summary>
internal static partial class TransportLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)TransportEventId.ListenerAccept,
        EventName = nameof(TransportEventId.ListenerAccept),
        Level = LogLevel.Debug,
        Message = "{Kind} listener {Endpoint} accepted a new connection")]
    internal static partial void LogListenerAccept(this ILogger logger, string kind, Endpoint endpoint);

    [LoggerMessage(
        EventId = (int)TransportEventId.ListenerAcceptException,
        EventName = nameof(TransportEventId.ListenerAcceptException),
        Level = LogLevel.Debug,
        Message = "{Kind} listener {Endpoint} failed to accept a new connection")]
    internal static partial void LogListenerAcceptException(
        this ILogger logger,
        Exception exception,
        string kind,
        Endpoint endpoint);

    [LoggerMessage(
        EventId = (int)TransportEventId.ListenerDispose,
        EventName = nameof(TransportEventId.ListenerDispose),
        Level = LogLevel.Information,
        Message = "{Kind} listener {Endpoint} shut down successfully")]
    internal static partial void LogListenerDispose(this ILogger logger, string kind, Endpoint endpoint);

    [LoggerMessage(
        EventId = (int)TransportEventId.ServerTransportListen,
        EventName = nameof(TransportEventId.ServerTransportListen),
        Level = LogLevel.Information,
        Message = "{Kind} server transport is listening for new connections on {Endpoint}...")]
    internal static partial void LogServerTransportListen(this ILogger logger, string kind, Endpoint endpoint);

    [LoggerMessage(
        EventId = (int)TransportEventId.ServerTransportListenException,
        EventName = nameof(TransportEventId.ServerTransportListenException),
        Level = LogLevel.Information,
        Message = "{Kind} server transport cannot listen for new connections on {Endpoint}")]
    internal static partial void LogServerTransportListenException(
        this ILogger logger,
        Exception exception,
        string kind,
        Endpoint endpoint);
}
