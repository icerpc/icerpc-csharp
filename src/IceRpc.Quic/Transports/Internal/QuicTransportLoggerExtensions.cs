// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports;

/// <summary>This class contains ILogger extension methods for logging calls to the transport connection APIs.</summary>
internal static partial class TransportLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)QuicTransportEventId.ConnectionAcceptFailed,
        EventName = nameof(QuicTransportEventId.ConnectionAcceptFailed),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' failed to accept a new connection")]
    internal static partial void LogQuicConnectionAcceptFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);
}
