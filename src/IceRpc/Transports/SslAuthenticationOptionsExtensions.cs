// Copyright (c) ZeroC, Inc.

using System.Net.Security;

namespace IceRpc.Transports;

/// <summary>Provides extension methods for <see cref="SslClientAuthenticationOptions" /> and <see
/// cref="SslServerAuthenticationOptions" /> to provide a method to clone authentication options.</summary>
public static class SslAuthenticationOptionsExtensions
{
    /// <summary>Makes a shallow copy of an SSL client authentication options.</summary>
    /// <param name="value">The options to copy.</param>
    /// <returns>The shallow copy.</returns>
    public static SslClientAuthenticationOptions Clone(this SslClientAuthenticationOptions value) =>
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

    /// <summary>Makes a shallow copy of an SSL server authentication options.</summary>
    /// <param name="value">The options to copy.</param>
    /// <returns>The shallow copy.</returns>
    public static SslServerAuthenticationOptions Clone(this SslServerAuthenticationOptions value) =>
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
