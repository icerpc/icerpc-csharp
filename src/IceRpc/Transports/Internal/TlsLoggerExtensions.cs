// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IceRpc.Transports.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging Tls messages.</summary>
    internal static partial class TlsLoggerExtensions
    {
        private static readonly Action<ILogger, Dictionary<string, string>, Exception> _tlsAuthenticationSucceeded =
            LoggerMessage.Define<Dictionary<string, string>>(
                LogLevel.Debug,
                new EventId((int)TlsEvent.TlsAuthenticationSucceeded, nameof(TlsEvent.TlsAuthenticationSucceeded)),
                "Tls authentication succeeded ({TlsInfo})");


        // TODO: log SslStream properties
        [LoggerMessage(
            EventId = (int)TlsEvent.TlsAuthenticationFailed,
            EventName = nameof(TlsEvent.TlsAuthenticationFailed),
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
