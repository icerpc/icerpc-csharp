// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.DependencyInjection;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports.Tcp;

internal static class SslTransportServiceCollectionExtensions
{
    extension(IServiceCollection services)
    {
        [System.Diagnostics.CodeAnalysis.SuppressMessage(
            "Security",
            "CA5359:Do Not Disable Certificate Validation",
            Justification = "The transport conformance tests do not rely on certificate validation")]

        internal IServiceCollection AddSslTest(int? listenBacklog) => services
            .AddTcpTest(listenBacklog)
            .AddSingleton(provider =>
                new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
                })
            .AddSingleton(provider => new SslServerAuthenticationOptions
            {
                ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile(
                    "server.p12",
                    password: null,
                    keyStorageFlags: X509KeyStorageFlags.Exportable)
            });
    }
}
