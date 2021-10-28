// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging TCP messages.</summary>
    internal static partial class TcpLoggerExtensions
    {
        /// <summary>The maximum (least verbose) log level used for TCP logging.</summary>
        internal const LogLevel MaxLogLevel = LogLevel.Debug;

        private static readonly Action<ILogger, Dictionary<string, string>, Exception> _tlsAuthenticationSucceeded =
            LoggerMessage.Define<Dictionary<string, string>>(
                LogLevel.Debug,
                new EventId((int)TcpEventIds.TlsAuthenticationSucceeded,
                             nameof(TcpEventIds.TlsAuthenticationSucceeded)),
                "Tls authentication succeeded ({TlsInfo})");

        [LoggerMessage(
            EventId = (int)TcpEventIds.ConnectionAccepted,
            EventName = nameof(TcpEventIds.ConnectionAccepted),
            Level = LogLevel.Debug,
            Message = "accepted tcp connection (ReceiveBufferSize={RcvSize}, SendBufferSize={SndSize})")]
        internal static partial void LogTcpNetworkConnectionAccepted(this ILogger logger, int rcvSize, int sndSize);

        [LoggerMessage(
            EventId = (int)TcpEventIds.ConnectionEstablished,
            EventName = nameof(TcpEventIds.ConnectionEstablished),
            Level = LogLevel.Debug,
            Message = "established tcp connection (ReceiveBufferSize={RcvSize}, SendBufferSize={SndSize})")]
        internal static partial void LogTcpNetworkConnectionEstablished(this ILogger logger, int rcvSize, int sndSize);

        // TODO: log SslStream properties
        [LoggerMessage(
            EventId = (int)TcpEventIds.TlsAuthenticationFailed,
            EventName = nameof(TcpEventIds.TlsAuthenticationFailed),
            Level = LogLevel.Debug,
            Message = "Tls authentication failed")]
        internal static partial void LogTlsAuthenticationFailed(this ILogger logger, Exception exception);

        internal static void LogTlsAuthenticationSucceeded(
            this ILogger logger,
            System.Net.Security.SslStream sslStream)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _tlsAuthenticationSucceeded(
                    logger,
                    new Dictionary<string, string>()
                    {
                        { "authenticated", $"{sslStream.IsAuthenticated}" },
                        { "encrypted", $"{sslStream.IsEncrypted}" },
                        { "signed", $"{sslStream.IsSigned}" },
                        { "mutually authenticated", $"{sslStream.IsMutuallyAuthenticated}" },
                        { "cipher", $"{sslStream.NegotiatedCipherSuite}" },
                        { "protocol", $"{sslStream.SslProtocol}" }
                    },
                    null!);
            }
        }
    }
}
