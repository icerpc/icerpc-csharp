// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

[Parallelizable(ParallelScope.All)]
public class SlicConformanceTests : MultiplexedTransportConformanceTests
{
    /// <summary>Creates the service collection used for Slic multiplexed transports for conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection()
    {
        var endpoint = new Endpoint(Protocol.IceRpc) { Host = "colochost" };
        var services = new ServiceCollection()
            .AddColocTransport()
            .AddSingleton(provider =>
            {
                var loggerFactory = provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
                var transport = provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();
                var listener = transport.Listen(
                    endpoint,
                    null,
                    loggerFactory.CreateLogger("IceRpc"));
                return listener;
            });

        services.
            TryAddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicServerTransport(
                    provider.GetRequiredService<IOptions<SlicTransportOptions>>().Value,
                    provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));

        services.
            TryAddSingleton<IClientTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicClientTransport(
                    provider.GetRequiredService<IOptions<SlicTransportOptions>>().Value,
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

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
