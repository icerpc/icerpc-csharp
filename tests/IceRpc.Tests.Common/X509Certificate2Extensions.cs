// Copyright (c) ZeroC, Inc.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Common;

/// <summary>Provides extension methods for <see cref="X509Certificate2"/>.</summary>
public static class X509Certificate2Extensions
{
    extension(X509Certificate2 source)
    {
        /// <summary>Creates a <see cref="SslClientAuthenticationOptions"/> from a root CA.</summary>
        public SslClientAuthenticationOptions ToClientAuthenticationOptions() =>
            new()
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
                {
                    if (certificate is X509Certificate2 peerCertificate)
                    {
                        using var customChain = new X509Chain();
                        customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        customChain.ChainPolicy.DisableCertificateDownloads = true;
                        customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        customChain.ChainPolicy.CustomTrustStore.Add(source);
                        return customChain.Build(peerCertificate);
                    }
                    else
                    {
                        return false;
                    }
                }
            };

        /// <summary>Creates a <see cref="SslServerAuthenticationOptions"/> from a server certificate.</summary>
        public SslServerAuthenticationOptions ToServerAuthenticationOptions() =>
            new()
            {
                ServerCertificateContext = SslStreamCertificateContext.Create(source, additionalCertificates: null)
            };
    }
}
