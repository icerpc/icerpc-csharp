// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.DependencyInjection;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports.Tcp;

internal static class SslTransportServiceCollectionExtensions
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "The transport conformance tests do not rely on certificate validation")]

    internal static IServiceCollection AddSslTest(this IServiceCollection services, int? listenBacklog) => services
        .AddTcpTest(listenBacklog)
        .AddSingleton(provider =>
            new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
            })
        .AddSingleton(provider => new SslServerAuthenticationOptions
        {
            ServerCertificate = X509CertificateLoader.LoadPkcs12FromFile("server.p12", password: null)
        });
}
