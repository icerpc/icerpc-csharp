// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the tls duplex transport.</summary>
[Parallelizable(ParallelScope.All)]
public class TlsTransportConformanceTests : TcpTransportConformanceTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "The transport conformance tests do not rely on certificate validation")]
    protected override IServiceCollection CreateServiceCollection()
    {
        var services = base.CreateServiceCollection();

        services.AddSingleton(provider =>
            new SslClientAuthenticationOptions
            {
                ClientCertificates = new X509CertificateCollection()
                    {
                        new X509Certificate2("../../../certs/client.p12", "password")
                    },
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
            });

        services.AddSingleton(provider => new SslServerAuthenticationOptions
        {
            ClientCertificateRequired = false,
            ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
        });

        return services;
    }
}
