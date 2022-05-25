// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

[Parallelizable(ParallelScope.All)]
public class SlicConformanceTests : MultiplexedTransportConformanceTests
{
    /// <summary>Creates the service collection used for Slic multiplexed transports for conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection()
            .AddColocTransport()
            .AddSlicTransport()
            .AddSingleton(typeof(Endpoint), new Endpoint(Protocol.IceRpc) { Host = "colochost" })
            .AddSingleton(provider =>
            {
                var transport = provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();
                var listener = transport.Listen(
                    (Endpoint)provider.GetRequiredService(typeof(Endpoint)),
                    null,
                    NullLogger.Instance);
                return listener;
            });

        services.TryAddSingleton(new MultiplexedTransportOptions());
        services.AddOptions<SlicTransportOptions>().Configure<MultiplexedTransportOptions>(
            (options, multiplexedTransportOptions) =>
            {
                if (multiplexedTransportOptions.BidirectionalStreamMaxCount is int bidirectionalStreamMaxCount)
                {
                    options.BidirectionalStreamMaxCount = bidirectionalStreamMaxCount;
                }

                if (multiplexedTransportOptions.UnidirectionalStreamMaxCount is int unidirectionalStreamMaxCount)
                {
                    options.UnidirectionalStreamMaxCount = unidirectionalStreamMaxCount;
                }
            });

        return services;
    }
}
