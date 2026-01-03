// Copyright (c) ZeroC, Inc.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Provides helper methods to create the authentication options used by the examples.
public static partial class Program
{
    /// <summary>Creates client authentication options with a custom certificate validation callback that uses the
    /// specified Root CA.</summary>
    /// <param name="rootCA">The Root CA certificate.</param>
    /// <returns>The client authentication options.</returns>
    public static SslClientAuthenticationOptions CreateClientAuthenticationOptions(X509Certificate2 rootCA) =>
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
                    customChain.ChainPolicy.CustomTrustStore.Add(rootCA);
                    return customChain.Build(peerCertificate);
                }
                else
                {
                    return false;
                }
            }
        };

    /// <summary>Creates server authentication options for the specified server certificate.</summary>
    /// <param name="serverCertificate">The server certificate.</param>
    /// <returns>The server authentication options.</returns>
    public static SslServerAuthenticationOptions CreateServerAuthenticationOptions(
        X509Certificate2 serverCertificate) =>
        new()
        {
            ServerCertificateContext =
                SslStreamCertificateContext.Create(serverCertificate, additionalCertificates: null)
        };
}
