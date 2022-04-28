// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports.Tests;

/// <summary>Conformance tests for the tls simple transport.</summary>
[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public class TlsTransportConformanceTests : SimpleTransportConformanceTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "The transport conformance tests do not rely on certificate validation")]
    protected override IServiceCollection CreateServiceCollection() =>
        new ServiceCollection()
            .UseTcp()
            .AddScoped(_ => new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection()
                {
                    new X509Certificate2("../../../certs/client.p12", "password")
                },
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
            })
            .AddScoped(_ => new SslServerAuthenticationOptions
            {
                ClientCertificateRequired = false,
                ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
            });
}
