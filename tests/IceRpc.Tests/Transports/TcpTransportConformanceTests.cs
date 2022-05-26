// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

/// <summary>Conformance tests for the tcp simple transport.</summary>
[Parallelizable(ParallelScope.All)]
public class TcpTransportConformanceTests : SimpleTransportConformanceTests
{
    protected override IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection()
            .UseSimpleTransport();

        services.TryAddSingleton(new TcpServerTransportOptions());
        services.AddSingleton<IServerTransport<ISimpleNetworkConnection>>(
            provider => new TcpServerTransport(provider.GetRequiredService<TcpServerTransportOptions>()));

        services.TryAddSingleton(new TcpClientTransportOptions());
        services.AddScoped<IClientTransport<ISimpleNetworkConnection>>(
            provider => new TcpClientTransport(provider.GetRequiredService<TcpClientTransportOptions>()));

        services.AddScoped(
            typeof(Endpoint),
            provider =>
            {
                string protocol = provider.GetService<Protocol>()?.Name ?? "icerpc";
                return Endpoint.FromString($"{protocol}://127.0.0.1:0/");
            });

        return services;
    }
}
