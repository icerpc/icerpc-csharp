// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

namespace ZeroC.Ice
{
    internal static class SecurityLoggerExtensions
    {
        private const int TlsCertificateChainError = 0;
        private const int TlsCertificateValidationFailed = 1;
        private const int TlsConnectionCreated = 2;
        private const int TlsHostnameMismatch = 3;
        private const int TlsRemoteCertificateNotProvided = 4;
        private const int TlsRemoteCertificateNotProvidedIgnored = 5;

        private static readonly Action<ILogger, X509ChainStatusFlags, Exception> _tlsCertificateChainError =
            LoggerMessage.Define<X509ChainStatusFlags>(
                LogLevel.Error,
                new EventId(TlsCertificateChainError, nameof(TlsCertificateChainError)),
                "Tls certificate chain error {Status}");

        private static readonly Action<ILogger, Exception> _tlsCertificateValidationFailed = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(TlsCertificateValidationFailed, nameof(TlsCertificateValidationFailed)),
            "Tls certificate validation failed {Status}");

        private static readonly Action<ILogger, string, Dictionary<string, string>, Exception> _tlsConnectionCreated =
            LoggerMessage.Define<string, Dictionary<string, string>>(
                LogLevel.Error,
                new EventId(TlsConnectionCreated, nameof(TlsConnectionCreated)),
                "Tls connection summary {Description} {TlsConnectionInfo}");

        private static readonly Action<ILogger, Exception> _tlsHostnameMismatch = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(TlsHostnameMismatch, nameof(TlsHostnameMismatch)),
            "Tls certificate validation failed - Hostname mismatch");

        private static readonly Action<ILogger, Exception> _tlsRemoteCertificateNotProvided = LoggerMessage.Define(
            LogLevel.Error,
            new EventId(TlsRemoteCertificateNotProvided, nameof(TlsRemoteCertificateNotProvided)),
            "Tls certificate validation failed - remote certificate not provided");

        private static readonly Action<ILogger, Exception> _tlsRemoteCertificateNotProvidedIgnored =
            LoggerMessage.Define(
                LogLevel.Debug,
                new EventId(TlsRemoteCertificateNotProvidedIgnored, nameof(TlsRemoteCertificateNotProvidedIgnored)),
                "Tls certificate validation failed - remote certificate not provided (ignored)");

        internal static void LogTlsCertificateChainError(this ILogger logger, X509ChainStatusFlags status) =>
            _tlsCertificateChainError(logger, status, null!);

        internal static void LogTlsCertificateValidationFailed(this ILogger logger) =>
            _tlsCertificateValidationFailed(logger, null!);

        internal static void LogTlsConnectionCreated(
            this ILogger logger,
            string description,
            Dictionary<string, string> info) =>
            _tlsConnectionCreated(logger, description, info, null!);

        internal static void LogTlsHostnameMismatch(this ILogger logger) => _tlsHostnameMismatch(logger, null!);

        internal static void LogTlsRemoteCertificateNotProvided(this ILogger logger) =>
            _tlsRemoteCertificateNotProvided(logger, null!);

        internal static void LogTlsRemoteCertificateNotProvidedIgnored(this ILogger logger) =>
            _tlsRemoteCertificateNotProvidedIgnored(logger, null!);
    }
}
