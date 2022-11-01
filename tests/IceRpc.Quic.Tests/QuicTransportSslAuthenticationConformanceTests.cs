// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

/// <summary>Conformance tests for the Ssl duplex transport.</summary>
[Parallelizable(ParallelScope.All)]
public class QuicTransportSslAuthenticationConformanceTests : MultiplexedTransportSslAuthenticationConformanceTests
{
    protected override IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection()
            .AddMultiplexedTransportClientServerTest(new Uri("icerpc://127.0.0.1:0/"))
            .AddSingleton<IDuplexServerTransport>(provider => new TcpServerTransport(new TcpServerTransportOptions()))
            .AddSingleton<IDuplexClientTransport>(provider => new TcpClientTransport())
            .AddSingleton<IMultiplexedServerTransport>(
                    provider => new SlicServerTransport(
                        provider.GetRequiredService<IOptionsMonitor<SlicTransportOptions>>().Get("server"),
                        provider.GetRequiredService<IDuplexServerTransport>()))
            .AddSingleton<IMultiplexedClientTransport>(
                provider => new SlicClientTransport(
                    provider.GetRequiredService<IOptionsMonitor<SlicTransportOptions>>().Get("client"),
                    provider.GetRequiredService<IDuplexClientTransport>()));

        services.AddOptions<SlicTransportOptions>("client");
        services.AddOptions<SlicTransportOptions>("server");
        services.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.StreamErrorCodeConverter = IceRpcProtocol.Instance.MultiplexedStreamErrorCodeConverter);

        return services;
    }
}
