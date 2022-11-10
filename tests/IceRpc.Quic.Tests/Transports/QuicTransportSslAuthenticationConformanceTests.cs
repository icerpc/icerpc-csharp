// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Net.Quic;

namespace IceRpc.Tests.Transports;

[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
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
