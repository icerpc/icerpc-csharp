// Copyright (c) ZeroC, Inc.

using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Provides an extension method for <see cref="SslClientAuthenticationOptions" />.</summary>
public static class SslClientAuthenticationOptionsExtensions
{
    /// <summary>Extension methods for <see cref="SslClientAuthenticationOptions" />.</summary>
    /// <param name="value">The options to copy.</param>
    extension(SslClientAuthenticationOptions value)
    {
        /// <summary>Makes a shallow copy of an SSL client authentication options.</summary>
        /// <returns>The shallow copy.</returns>
        public SslClientAuthenticationOptions Clone() =>
            new()
            {
                AllowRenegotiation = value.AllowRenegotiation,
                ApplicationProtocols = value.ApplicationProtocols,
                CertificateRevocationCheckMode = value.CertificateRevocationCheckMode,
                CipherSuitesPolicy = value.CipherSuitesPolicy,
                ClientCertificates = value.ClientCertificates,
                EnabledSslProtocols = value.EnabledSslProtocols,
                EncryptionPolicy = value.EncryptionPolicy,
                LocalCertificateSelectionCallback = value.LocalCertificateSelectionCallback,
                RemoteCertificateValidationCallback = value.RemoteCertificateValidationCallback,
                TargetHost = value.TargetHost
            };
    }
}

/// <summary>Provides an extension method for <see cref="SslServerAuthenticationOptions" />.</summary>
public static class SslServerAuthenticationOptionsExtensions
{
    /// <summary>Extension methods for <see cref="SslServerAuthenticationOptions" />.</summary>
    /// <param name="value">The options to copy.</param>
    extension(SslServerAuthenticationOptions value)
    {
        /// <summary>Makes a shallow copy of an SSL server authentication options.</summary>
        /// <returns>The shallow copy.</returns>
        public SslServerAuthenticationOptions Clone() =>
            new()
            {
                AllowRenegotiation = value.AllowRenegotiation,
                ApplicationProtocols = value.ApplicationProtocols,
                CertificateRevocationCheckMode = value.CertificateRevocationCheckMode,
                CipherSuitesPolicy = value.CipherSuitesPolicy,
                ClientCertificateRequired = value.ClientCertificateRequired,
                EnabledSslProtocols = value.EnabledSslProtocols,
                EncryptionPolicy = value.EncryptionPolicy,
                RemoteCertificateValidationCallback = value.RemoteCertificateValidationCallback,
                ServerCertificate = value.ServerCertificate,
                ServerCertificateContext = value.ServerCertificateContext,
                ServerCertificateSelectionCallback = value.ServerCertificateSelectionCallback
            };
    }
}
