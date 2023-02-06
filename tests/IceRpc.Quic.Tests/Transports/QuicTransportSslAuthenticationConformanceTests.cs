// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Net.Quic;

namespace IceRpc.Tests.Transports;

public class QuicTransportSslAuthenticationConformanceTests : MultiplexedTransportSslAuthenticationConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("Quic is not supported on this platform");
        }
    }

    protected override IServiceCollection CreateServiceCollection() =>
        new ServiceCollection()
            .AddMultiplexedTransportClientServerTest(new Uri("icerpc://127.0.0.1:0/"))
            .AddSingleton<IDuplexServerTransport>(provider => new TcpServerTransport(new TcpServerTransportOptions()))
            .AddSingleton<IDuplexClientTransport>(provider => new TcpClientTransport())
            .AddSingleton<IMultiplexedServerTransport>(provider => new QuicServerTransport())
            .AddSingleton<IMultiplexedClientTransport>(provider => new QuicClientTransport());
}
