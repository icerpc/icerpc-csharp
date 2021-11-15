// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging UDP messages.</summary>
    internal static partial class UdpLoggerExtensions
    {
        /// <summary>The maximum (least verbose) log level used for UDP logging.</summary>
        internal const LogLevel MaxLogLevel = LogLevel.Debug;

        [LoggerMessage(
            EventId = (int)UdpEventIds.Connect,
            EventName = nameof(UdpEventIds.Connect),
            Level = LogLevel.Debug,
            Message = "udp connect completed successfully (ReceiveBufferSize={RcvSize}, SendBufferSize={SndSize}")]
        internal static partial void LogUdpConnect(this ILogger logger, int rcvSize, int sndSize);
    }
}
