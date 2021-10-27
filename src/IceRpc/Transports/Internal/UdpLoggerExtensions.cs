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
            EventId = (int)UdpEventIds.StartReceivingDatagrams,
            EventName = nameof(UdpEventIds.StartReceivingDatagrams),
            Level = LogLevel.Debug,
            Message = "starting to receive udp datagrams (ReceiveBufferSize={RcvSize}, SendBufferSize={SndSize}")]
        internal static partial void LogUdpStartReceivingDatagrams(this ILogger logger, int rcvSize, int sndSize);

        [LoggerMessage(
            EventId = (int)UdpEventIds.StartSendingDatagrams,
            EventName = nameof(UdpEventIds.StartSendingDatagrams),
            Level = LogLevel.Debug,
            Message = "starting to send udp datagrams (ReceiveBufferSize={RcvSize}, SendBufferSize={SndSize}")]
        internal static partial void LogUdpStartSendingDatagrams(this ILogger logger, int rcvSize, int sndSize);
    }
}
