// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

public static class TcpTransportServiceCollectionExtensions
{
    public static ServiceCollection UseTcp(this ServiceCollection serviceCollection)
    {
        serviceCollection.UseSimpleTransport();

        serviceCollection.AddScoped<IServerTransport<ISimpleNetworkConnection>>(
            provider => new TcpServerTransport(
                provider.GetService<TcpServerTransportOptions>() ?? new TcpServerTransportOptions()));

        serviceCollection.AddScoped<IClientTransport<ISimpleNetworkConnection>>(
            provider => new TcpClientTransport(
                provider.GetService<TcpClientTransportOptions>() ?? new TcpClientTransportOptions()));

        serviceCollection.AddScoped(typeof(Endpoint), provider => Endpoint.FromString("icerpc://127.0.0.1:0/"));

        return serviceCollection;
    }
}

/// <summary>Conformance tests for the tcp simple transport.</summary>
[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public class TcpTransportConformanceTests : SimpleTransportConformanceTests
{
    protected override ServiceCollection CreateServiceCollection() => new ServiceCollection().UseTcp();
}
