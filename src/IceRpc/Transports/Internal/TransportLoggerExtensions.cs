// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal;

/// <summary>This class contains ILogger extension methods for logging calls to the transport APIs.</summary>
internal static partial class TransportLoggerExtensions
{
    private static readonly Func<ILogger, long, string, string, IDisposable> _multiplexedStreamScope =
        LoggerMessage.DefineScope<long, string, string>(
            "MultiplexedStream:{ID}, InitiatedBy:{InitiatedBy}, Kind:{Kind}");

    [LoggerMessage(
        EventId = (int)TransportEventId.DuplexConnectionRead,
        EventName = nameof(TransportEventId.DuplexConnectionRead),
        Level = LogLevel.Trace,
        Message = "Duplex connection read {Size} bytes: {Data}")]
    internal static partial void LogDuplexConnectionRead(this ILogger logger, int size, string data);

    [LoggerMessage(
        EventId = (int)TransportEventId.DuplexConnectionWrite,
        EventName = nameof(TransportEventId.DuplexConnectionWrite),
        Level = LogLevel.Trace,
        Message = "Duplex connection wrote {Size} bytes: {Data}")]
    internal static partial void LogDuplexConnectionWrite(this ILogger logger, int size, string data);

    [LoggerMessage(
        EventId = (int)TransportEventId.DuplexConnectionShutdown,
        EventName = nameof(TransportEventId.DuplexConnectionShutdown),
        Level = LogLevel.Trace,
        Message = "Duplex connection shut down successfully")]
    internal static partial void LogDuplexConnectionShutdown(this ILogger logger);

    [LoggerMessage(
        EventId = (int)TransportEventId.DuplexConnectionShutdownException,
        EventName = nameof(TransportEventId.DuplexConnectionShutdownException),
        Level = LogLevel.Trace,
        Message = "Duplex connection failed to shut down")]
    internal static partial void LogDuplexConnectionShutdownException(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = (int)TransportEventId.ListenerAccept,
        EventName = nameof(TransportEventId.ListenerAccept),
        Level = LogLevel.Debug,
        Message = "{Kind} listener {ServerAddress} accepted a new connection")]
    internal static partial void LogListenerAccept(this ILogger logger, string kind, ServerAddress serverAddress);

    [LoggerMessage(
        EventId = (int)TransportEventId.ListenerAcceptException,
        EventName = nameof(TransportEventId.ListenerAcceptException),
        Level = LogLevel.Debug,
        Message = "{Kind} listener {ServerAddress} failed to accept a new connection")]
    internal static partial void LogListenerAcceptException(
        this ILogger logger,
        Exception exception,
        string kind,
        ServerAddress serverAddress);

    [LoggerMessage(
        EventId = (int)TransportEventId.ListenerDispose,
        EventName = nameof(TransportEventId.ListenerDispose),
        Level = LogLevel.Information,
        Message = "{Kind} listener {ServerAddress} shut down successfully")]
    internal static partial void LogListenerDispose(this ILogger logger, string kind, ServerAddress serverAddress);

    [LoggerMessage(
        EventId = (int)TransportEventId.MultiplexedConnectionAcceptStream,
        EventName = nameof(TransportEventId.MultiplexedConnectionAcceptStream),
        Level = LogLevel.Trace,
        Message = "Multiplexed connection accepted a new {Kind} stream")]
    internal static partial void LogMultiplexedConnectionAcceptStream(this ILogger logger, string kind);

    [LoggerMessage(
        EventId = (int)TransportEventId.MultiplexedConnectionAcceptStreamException,
        EventName = nameof(TransportEventId.MultiplexedConnectionAcceptStreamException),
        Level = LogLevel.Trace,
        Message = "Multiplexed connection failed to accept a new stream")]
    internal static partial void LogMultiplexedConnectionAcceptStreamException(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = (int)TransportEventId.MultiplexedConnectionCreateStream,
        EventName = nameof(TransportEventId.MultiplexedConnectionCreateStream),
        Level = LogLevel.Trace,
        Message = "Multiplexed connection created a new {Kind} stream")]
    internal static partial void LogMultiplexedConnectionCreateStream(this ILogger logger, string kind);

    [LoggerMessage(
        EventId = (int)TransportEventId.MultiplexedConnectionCreateStreamException,
        EventName = nameof(TransportEventId.MultiplexedConnectionCreateStreamException),
        Level = LogLevel.Trace,
        Message = "Multiplexed connection failed to accept a new {Kind} stream")]
    internal static partial void LogMultiplexedConnectionCreateStreamException(
        this ILogger logger,
        Exception exception,
        string kind);

    [LoggerMessage(
        EventId = (int)TransportEventId.MultiplexedConnectionShutdown,
        EventName = nameof(TransportEventId.MultiplexedConnectionShutdown),
        Level = LogLevel.Trace,
        Message = "Multiplexed connection shut down successfully: {Message}")]
    internal static partial void LogMultiplexedConnectionShutdown(this ILogger logger, string message);

    [LoggerMessage(
        EventId = (int)TransportEventId.MultiplexedConnectionShutdownException,
        EventName = nameof(TransportEventId.MultiplexedConnectionShutdownException),
        Level = LogLevel.Trace,
        Message = "Multiplexed connection failed to shut down")]
    internal static partial void LogMultiplexedConnectionShutdownException(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = (int)TransportEventId.ServerTransportListen,
        EventName = nameof(TransportEventId.ServerTransportListen),
        Level = LogLevel.Information,
        Message = "{Kind} server transport is listening for new connections on {ServerAddress}...")]
    internal static partial void LogServerTransportListen(this ILogger logger, string kind, ServerAddress serverAddress);

    [LoggerMessage(
        EventId = (int)TransportEventId.ServerTransportListenException,
        EventName = nameof(TransportEventId.ServerTransportListenException),
        Level = LogLevel.Information,
        Message = "{Kind} server transport cannot listen for new connections on {ServerAddress}")]
    internal static partial void LogServerTransportListenException(
        this ILogger logger,
        Exception exception,
        string kind,
        ServerAddress serverAddress);

    internal static IDisposable StartMultiplexedStreamScope(this ILogger logger, IMultiplexedStream stream) =>
        stream.IsStarted ? StartMultiplexedStreamScope(logger, stream.Id) :
            // Client stream is not started yet
            stream.IsBidirectional switch
            {
                false => _multiplexedStreamScope(logger, -1, "Client", "Unidirectional"),
                true => _multiplexedStreamScope(logger, -1, "Client", "Bidirectional")
            };

    internal static IDisposable StartMultiplexedStreamScope(this ILogger logger, long streamId) =>
        (streamId % 4) switch
        {
            0 => _multiplexedStreamScope(logger, streamId, "Client", "Bidirectional"),
            1 => _multiplexedStreamScope(logger, streamId, "Server", "Bidirectional"),
            2 => _multiplexedStreamScope(logger, streamId, "Client", "Unidirectional"),
            _ => _multiplexedStreamScope(logger, streamId, "Server", "Unidirectional")
        };
}
