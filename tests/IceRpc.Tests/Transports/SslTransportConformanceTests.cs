// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the Ssl transport.</summary>
[Parallelizable(ParallelScope.All)]
public class SslTransportConformanceTests : TcpTransportConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() =>
        SslTransportConformanceTestsServiceCollection.Create();
}

/// <summary>Conformance tests for the Ssl transport listener.</summary>
[Parallelizable(ParallelScope.All)]
public class SslListenerTransportConformanceTests : TcpListenerTransportConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() =>
        SslTransportConformanceTestsServiceCollection.Create();
}

internal static class SslTransportConformanceTestsServiceCollection
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Security",
        "CA5359:Do Not Disable Certificate Validation",
        Justification = "The transport conformance tests do not rely on certificate validation")]

    internal static IServiceCollection Create()
    {
        var services = TcpTransportConformanceTestsServiceCollection.Create();

        services.AddSingleton(provider =>
            new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (sender, certificate, chain, errors) => true
            });

        services.AddSingleton(provider => new SslServerAuthenticationOptions
        {
            ServerCertificate = new X509Certificate2("../../../certs/server.p12", "password")
        });
        return services;
    }
}
