// Copyright (c) ZeroC, Inc.

using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

// Provides helper methods to create the authentication options used by the examples.
public static partial class Program
{
    private static readonly X509Certificate2 RootCA =
        X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

    /// <summary>Creates client authentication options with a custom certificate validation callback that uses the test
    /// certificates' Root CA.</summary>
    /// <returns>The client authentication options.</returns>
    public static SslClientAuthenticationOptions CreateClientAuthenticationOptions() =>
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
                    customChain.ChainPolicy.CustomTrustStore.Add(RootCA);
                    return customChain.Build(peerCertificate);
                }
                else
                {
                    return false;
                }
            }
        };

    /// <summary>Creates server authentication options with the server.p12 test certificate.</summary>
    /// <returns>The server authentication options.</returns>
    public static SslServerAuthenticationOptions CreateServerAuthenticationOptions() =>
        new()
        {
            ServerCertificateContext = SslStreamCertificateContext.Create(
                X509CertificateLoader.LoadPkcs12FromFile(
                    "../../../../certs/server.p12",
                    password: null,
                    keyStorageFlags: X509KeyStorageFlags.Exportable),
                additionalCertificates: null)
        };
}
