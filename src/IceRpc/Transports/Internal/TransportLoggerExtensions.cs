// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System.Net;

namespace IceRpc.Transports.Internal;

/// <summary>This class contains ILogger extension methods for logging calls to the transport APIs.</summary>
internal static partial class TransportLoggerExtensions
{
    private static readonly Func<ILogger, long, string, string, IDisposable> _multiplexedStreamScope =
        LoggerMessage.DefineScope<long, string, string>(
            "MultiplexedStream(ID={ID}, InitiatedBy={InitiatedBy}, Kind={Kind})");

    [LoggerMessage(
        EventId = (int)TransportEventIds.ListenerAcceptFailed,
        EventName = nameof(TransportEventIds.ListenerAcceptFailed),
        Level = LogLevel.Error,
        Message = "listener '{endpoint}' failed to accept a new connection")]
    internal static partial void LogListenerAcceptFailed(
        this ILogger logger,
        Endpoint endpoint,
        Exception ex);

    [LoggerMessage(
        EventId = (int)TransportEventIds.ListenerCreated,
        EventName = nameof(TransportEventIds.ListenerCreated),
        Level = LogLevel.Information,
        Message = "listener '{endpoint}' started")]
    internal static partial void LogListenerCreated(this ILogger logger, Endpoint endpoint);

    [LoggerMessage(
        EventId = (int)TransportEventIds.ListenerDisposed,
        EventName = nameof(TransportEventIds.ListenerDisposed),
        Level = LogLevel.Information,
        Message = "listener '{endpoint}' shut down")]
    internal static partial void LogListenerDispose(this ILogger logger, Endpoint endpoint);

    [LoggerMessage(
        EventId = (int)TransportEventIds.MultiplexedStreamRead,
        EventName = nameof(TransportEventIds.MultiplexedStreamRead),
        Level = LogLevel.Trace,
        Message = "read {Size} bytes from multiplexed stream ({Data})")]
    internal static partial void LogMultiplexedStreamRead(
        this ILogger logger,
        int size,
        string data);

    [LoggerMessage(
        EventId = (int)TransportEventIds.MultiplexedConnectionShutdown,
        EventName = nameof(TransportEventIds.MultiplexedConnectionShutdown),
        Level = LogLevel.Trace,
        Message = "connection shutdown")]
    internal static partial void LogMultiplexedConnectionShutdown(
        this ILogger logger,
        Exception exception);

    [LoggerMessage(
        EventId = (int)TransportEventIds.MultiplexedStreamWrite,
        EventName = nameof(TransportEventIds.MultiplexedStreamWrite),
        Level = LogLevel.Trace,
        Message = "wrote {Size} bytes to multiplexed stream ({Data})")]
    internal static partial void LogMultiplexedStreamWrite(
        this ILogger logger,
        int size,
        string data);

    [LoggerMessage(
        EventId = (int)TransportEventIds.TransportConnectionConnect,
        EventName = nameof(TransportEventIds.TransportConnectionConnect),
        Level = LogLevel.Debug,
        Message = "transport connection established: LocalNetworkAddress={LocalNetworkAddress}, RemoteNetworkAddress={RemoteNetworkAddress}")]
    internal static partial void LogTransportConnectionConnect(
        this ILogger logger,
        EndPoint? localNetworkAddress,
        EndPoint? remoteNetworkAddress);

    [LoggerMessage(
        EventId = (int)TransportEventIds.TransportConnectionConnectFailed,
        EventName = nameof(TransportEventIds.TransportConnectionConnectFailed),
        Level = LogLevel.Debug,
        Message = "transport connection establishment failed")]
    internal static partial void LogTransportConnectionConnectFailed(this ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = (int)TransportEventIds.TransportConnectionDispose,
        EventName = nameof(TransportEventIds.TransportConnectionDispose),
        Level = LogLevel.Debug,
        Message = "transport connection disposed")]
    internal static partial void LogTransportConnectionDispose(this ILogger logger);

    [LoggerMessage(
        EventId = (int)TransportEventIds.DuplexConnectionRead,
        EventName = nameof(TransportEventIds.DuplexConnectionRead),
        Level = LogLevel.Trace,
        Message = "read {Size} bytes from duplex connection ({Data})")]
    internal static partial void LogDuplexConnectionRead(this ILogger logger, int size, string data);

    [LoggerMessage(
        EventId = (int)TransportEventIds.DuplexConnectionShutdown,
        EventName = nameof(TransportEventIds.DuplexConnectionShutdown),
        Level = LogLevel.Trace,
        Message = "duplex connection shutdown")]
    internal static partial void LogDuplexConnectionShutdown(this ILogger logger);

    [LoggerMessage(
        EventId = (int)TransportEventIds.DuplexConnectionWrite,
        EventName = nameof(TransportEventIds.DuplexConnectionWrite),
        Level = LogLevel.Trace,
        Message = "wrote {Size} bytes to duplex connection ({Data})")]
    internal static partial void LogDuplexConnectionWrite(this ILogger logger, int size, string data);

    internal static IDisposable StartMultiplexedStreamScope(this ILogger logger, IMultiplexedStream stream) =>
        stream.IsStarted ?
            StartMultiplexedStreamScope(logger, stream.Id) :
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
