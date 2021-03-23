// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Net.Security;

namespace IceRpc
{
    internal static class SslAuthenticationOptionsExtensions
    {
        internal static SslClientAuthenticationOptions Clone(this SslClientAuthenticationOptions value) =>
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

        internal static SslServerAuthenticationOptions Clone(this SslServerAuthenticationOptions value) =>
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
