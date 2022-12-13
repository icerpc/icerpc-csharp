// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports;

/// <summary>This class contains ILogger extension methods for logging calls to the transport connection APIs.</summary>
internal static partial class TcpTransportLoggerExtensions
{
    [LoggerMessage(
        EventId = (int)TcpTransportEventId.ConnectionAcceptFailed,
        EventName = nameof(TcpTransportEventId.ConnectionAcceptFailed),
        Level = LogLevel.Trace,
        Message = "Listener '{ServerAddress}' failed to accept a new connection")]
    internal static partial void LogTcpConnectionAcceptFailed(
        this ILogger logger,
        ServerAddress serverAddress,
        Exception exception);
}
