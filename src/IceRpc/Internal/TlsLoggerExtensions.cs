﻿// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace IceRpc.Internal
{
    /// <summary>This class contains ILogger extensions methods for logging Tls messages.</summary>
    internal static class TlsLoggerExtensions
    {
        private const int BaseEventId = LoggerExtensions.TlsBaseEventId;
        private const int TlsAuthenticationSucceeded = BaseEventId + 2;

        private static readonly Action<ILogger, Dictionary<string, string>, Exception> _tlsAuthenticationSucceeded =
            LoggerMessage.Define<Dictionary<string, string>>(
                LogLevel.Debug,
                new EventId(TlsAuthenticationSucceeded, nameof(TlsAuthenticationSucceeded)),
                "Tls authentication succeeded ({TlsConnectionInfo})");

        private static readonly Action<ILogger, Exception> _tlsAuthenticationFailed =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(TlsAuthenticationSucceeded, nameof(TlsAuthenticationSucceeded)),
                "Tls authentication failed");

        internal static void LogTlsAuthenticationFailed(
            this ILogger logger,
            System.Net.Security.SslStream _,
            Exception exception) =>
            // TODO: log SslStream properties
            _tlsAuthenticationFailed(logger, exception);

        internal static void LogTlsAuthenticationSucceeded(
            this ILogger logger,
            System.Net.Security.SslStream sslStream)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                _tlsAuthenticationSucceeded(logger, new Dictionary<string, string>()
                    {
                        { "authenticated", $"{sslStream.IsAuthenticated}" },
                        { "encrypted", $"{sslStream.IsEncrypted}" },
                        { "signed", $"{sslStream.IsSigned}" },
                        { "mutually authenticated", $"{sslStream.IsMutuallyAuthenticated}" },
                        { "cipher", $"{sslStream.NegotiatedCipherSuite}" },
                        { "protocol", $"{sslStream.SslProtocol}" }
                    }, null!);
            }
        }
    }
}
