// Copyright (c) ZeroC, Inc.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Provides helper methods to create the authentication options used by the examples.
public static partial class Program
{
    /// <summary>Creates client authentication options with a custom certificate validation callback that uses the
    /// specified root CA.</summary>
    /// <param name="rootCA">The root CA certificate.</param>
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
    /// <param name="serverCertificate">The server certificate. It must include a private key.</param>
    /// <returns>The server authentication options.</returns>
    /// <remarks>Do not dispose the server certificate during the lifetime of the returned server authentication
    /// options.</remarks>
    /// <seealso cref="SslStreamCertificateContext.Create(X509Certificate2, X509Certificate2[])"/>
    public static SslServerAuthenticationOptions CreateServerAuthenticationOptions(
        X509Certificate2 serverCertificate) =>
        new()
        {
            ServerCertificateContext =
                SslStreamCertificateContext.Create(serverCertificate, additionalCertificates: null)
        };
}
