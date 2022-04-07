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
    public override ServiceCollection CreateServiceCollection() => new SlicServiceCollection();
}

public class SlicServiceCollection : ServiceCollection
{
    public SlicServiceCollection()
    {
        this.AddScoped(_ => new ColocTransport());

        this.AddScoped<IServerTransport<IMultiplexedNetworkConnection>>(provider =>
        {
            var colocTransport = provider.GetRequiredService<ColocTransport>();
            var serverOptions = new SlicServerTransportOptions();
            var multiplexedTransportOptions = provider.GetRequiredService<MultiplexedTransportOptions>();
            if (multiplexedTransportOptions != null)
            {
                serverOptions.BidirectionalStreamMaxCount = multiplexedTransportOptions.BidirectionalStreamMaxCount;
                serverOptions.UnidirectionalStreamMaxCount = multiplexedTransportOptions.UnidirectionalStreamMaxCount;
            }
            serverOptions.SimpleServerTransport = colocTransport.ServerTransport;
            return new SlicServerTransport(serverOptions);
        });

        this.AddScoped<IClientTransport<IMultiplexedNetworkConnection>>(provider =>
        {
            var colocTransport = provider.GetRequiredService<ColocTransport>();
            var clientOptions = new SlicClientTransportOptions();
            var multiplexedTransportOptions = provider.GetRequiredService<MultiplexedTransportOptions>();
            if (multiplexedTransportOptions != null)
            {
                clientOptions.BidirectionalStreamMaxCount = multiplexedTransportOptions.BidirectionalStreamMaxCount;
                clientOptions.UnidirectionalStreamMaxCount = multiplexedTransportOptions.UnidirectionalStreamMaxCount;
            }
            clientOptions.SimpleClientTransport = colocTransport.ClientTransport;
            return new SlicClientTransport(clientOptions);
        });

        this.AddScoped(provider =>
        {
            var serverTransport = provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();
            return serverTransport.Listen(
                Endpoint.FromString($"icerpc://{Guid.NewGuid()}/"),
                null,
                NullLogger.Instance);
        });

        this.AddScoped(_ => new MultiplexedTransportOptions());
    }
}
