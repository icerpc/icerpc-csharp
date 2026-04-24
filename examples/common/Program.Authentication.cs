// Copyright (c) ZeroC, Inc.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

/// <summary>Provides helper methods to create the authentication options used by the examples.</summary>
internal static partial class Program
{
    /// <summary>Creates client authentication options that trust only the specified root CA.</summary>
    /// <param name="rootCA">The root CA certificate.</param>
    /// <returns>The client authentication options.</returns>
    public static SslClientAuthenticationOptions CreateClientAuthenticationOptions(X509Certificate2 rootCA) =>
        new()
        {
            CertificateChainPolicy = new X509ChainPolicy
            {
                RevocationMode = X509RevocationMode.NoCheck,
                DisableCertificateDownloads = true,
                TrustMode = X509ChainTrustMode.CustomRootTrust,
                CustomTrustStore = { rootCA }
            }
        };

    /// <summary>Creates server authentication options for the specified server certificate.</summary>
    /// <param name="serverCertificate">The server certificate. It must include a private key.</param>
    /// <returns>The server authentication options.</returns>
    /// <remarks>Do not dispose the server certificate during the lifetime of the returned server authentication
    /// options.</remarks>
    public static SslServerAuthenticationOptions CreateServerAuthenticationOptions(
        X509Certificate2 serverCertificate) =>
        new()
        {
            ServerCertificateContext =
                SslStreamCertificateContext.Create(serverCertificate, additionalCertificates: null)
        };
}
