// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System;
using System.Diagnostics;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the Ssl duplex transport.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("macOS")]
[System.Runtime.Versioning.SupportedOSPlatform("linux")]
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
[Parallelizable(ParallelScope.All)]
public class QuicTransportSslAuthenticationConformanceTests : MultiplexedTransportSslAuthenticationConformanceTests
{
    [OneTimeSetUp]
    public void FixtureSetUp()
    {
        if (!QuicConnection.IsSupported)
        {
            Assert.Ignore("Quic is not supported on this platform");
        }
        Trace.Listeners.Add(new ConsoleTraceListener());
    }

    protected override IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection()
            .AddSslAuthenticationOptions()
            .AddMultiplexedTransportClientServerTest(new Uri("icerpc://127.0.0.1:0/"))
            .AddSingleton(provider =>
            .AddSingleton<IMultiplexedServerTransport>(provider =>
                new QuicServerTransport(
                    provider.GetRequiredService<IOptionsMonitor<QuicServerTransportOptions>>().Get("server")))
            .AddSingleton<IMultiplexedClientTransport>(provider =>
                new QuicClientTransport(
                    provider.GetRequiredService<IOptionsMonitor<QuicClientTransportOptions>>().Get("client")));

        services.AddOptions<QuicServerTransportOptions>("client");
        services.AddOptions<QuicClientTransportOptions>("server");
        services.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.StreamErrorCodeConverter = IceRpcProtocol.Instance.MultiplexedStreamErrorCodeConverter);

        return services;
    }
}
