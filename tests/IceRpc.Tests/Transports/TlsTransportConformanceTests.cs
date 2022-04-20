// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Transports.Tests;

public class TlsTransportServiceCollection : SimpleTransportServiceCollection
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "The transport tests do not rely on certificate validation")]
    public TlsTransportServiceCollection()
    {
        this.AddScoped(_ => new SslClientAuthenticationOptions
        {
            ClientCertificates = new X509CertificateCollection()
            {
                new X509Certificate2("../../../certs/client.p12", "password")
            },
            RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true,
        });
        this.AddScoped(_ => new SslServerAuthenticationOptions
        {
            ClientCertificateRequired = false,
            ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
        });
        this.AddScoped<IServerTransport<ISimpleNetworkConnection>>(_ => new TcpServerTransport());
        this.AddScoped<IClientTransport<ISimpleNetworkConnection>>(_ => new TcpClientTransport());
        this.AddScoped(typeof(Endpoint), provider => Endpoint.FromString("icerpc://127.0.0.1:0/"));
    }
}

/// <summary>Conformance tests for the tls simple transport.</summary>
[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public class TlsTransportConformanceTests : SimpleTransportConformanceTests
{
    protected override ServiceCollection CreateServiceCollection() => new TlsTransportServiceCollection();
}
