// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;

namespace IceRpc.Transports.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging TCP messages.</summary>
    internal static partial class TcpLoggerExtensions
    {
        /// <summary>The maximum (least verbose) log level used for TCP logging.</summary>
        internal const LogLevel MaxLogLevel = LogLevel.Debug;

        private static readonly Action<ILogger, Dictionary<string, string>, Exception> _tlsAuthentication =
            LoggerMessage.Define<Dictionary<string, string>>(
                LogLevel.Debug,
                new EventId((int)TcpEventIds.TlsAuthentication,
                             nameof(TcpEventIds.TlsAuthentication)),
                "Tls authentication succeeded ({TlsInfo})");

        [LoggerMessage(
            EventId = (int)TcpEventIds.Connect,
            EventName = nameof(TcpEventIds.Connect),
            Level = LogLevel.Debug,
            Message = "tcp connection established (ReceiveBufferSize={RcvSize}, SendBufferSize={SndSize})")]
        internal static partial void LogTcpConnect(this ILogger logger, int rcvSize, int sndSize);

        internal static void LogTlsAuthentication(
            this ILogger logger,
            System.Net.Security.SslStream sslStream)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _tlsAuthentication(
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

        // TODO: log SslStream properties
        [LoggerMessage(
            EventId = (int)TcpEventIds.TlsAuthenticationFailed,
            EventName = nameof(TcpEventIds.TlsAuthenticationFailed),
            Level = LogLevel.Debug,
            Message = "Tls authentication failed")]
        internal static partial void LogTlsAuthenticationFailed(this ILogger logger, Exception exception);
    }
}
