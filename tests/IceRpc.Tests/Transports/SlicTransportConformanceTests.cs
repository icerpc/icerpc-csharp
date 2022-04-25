// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public class SlicConformanceTests : MultiplexedTransportConformanceTests
{
    /// <summary>Creates the service collection used for Slic multiplexed transports for conformance testing.</summary>
    protected override ServiceCollection CreateServiceCollection() => new ServiceCollection().UseSlic();
}

public static class SlicServiceCollectionExtensions
{
    public static ServiceCollection UseSlic(this ServiceCollection serviceCollection)
    {
        serviceCollection.UseColoc();

        serviceCollection.AddScoped<IServerTransport<IMultiplexedNetworkConnection>>(provider =>
        {
            var simpleServerTransport = provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();
            var serverOptions = provider.GetService<SlicServerTransportOptions>() ?? new SlicServerTransportOptions();
            var multiplexedTransportOptions = provider.GetRequiredService<MultiplexedTransportOptions>();
            if (multiplexedTransportOptions.BidirectionalStreamMaxCount is int bidirectionalStreamMaxCount)
            {
                serverOptions.BidirectionalStreamMaxCount = bidirectionalStreamMaxCount;
            }
            if (multiplexedTransportOptions.UnidirectionalStreamMaxCount is int unidirectionalStreamMaxCount)
            {
                serverOptions.UnidirectionalStreamMaxCount = unidirectionalStreamMaxCount;
            }
            serverOptions.SimpleServerTransport = simpleServerTransport;
            return new SlicServerTransport(serverOptions);
        });

        serviceCollection.AddScoped<IClientTransport<IMultiplexedNetworkConnection>>(provider =>
        {
            var simpleClientTransport = provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();
            var clientOptions = provider.GetService<SlicClientTransportOptions>() ?? new SlicClientTransportOptions();
            var multiplexedTransportOptions = provider.GetRequiredService<MultiplexedTransportOptions>();
            if (multiplexedTransportOptions.BidirectionalStreamMaxCount is int bidirectionalStreamMaxCount)
            {
                clientOptions.BidirectionalStreamMaxCount = bidirectionalStreamMaxCount;
            }
            if (multiplexedTransportOptions.UnidirectionalStreamMaxCount is int unidirectionalStreamMaxCount)
            {
                clientOptions.UnidirectionalStreamMaxCount = unidirectionalStreamMaxCount;
            }
            clientOptions.SimpleClientTransport = simpleClientTransport;
            return new SlicClientTransport(clientOptions);
        });

        serviceCollection.AddScoped(provider =>
        {
            var serverTransport = provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();
            return serverTransport.Listen(
                (Endpoint)provider.GetRequiredService(typeof(Endpoint)),
                null,
                NullLogger.Instance);
        });

        serviceCollection.UseTransportOptions();

        return serviceCollection;
    }
}
