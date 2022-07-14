// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

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
                var transport = provider.GetRequiredService<IServerTransport<IMultiplexedTransportConnection>>();
                var listener = transport.Listen(
                    endpoint,
                    null,
                    loggerFactory.CreateLogger("IceRpc"));
                return listener;
            });

        services.
            TryAddSingleton<IServerTransport<IMultiplexedTransportConnection>>(
                provider => new SlicServerTransport(
                    provider.GetRequiredService<IOptionsMonitor<SlicTransportOptions>>().Get("server"),
                    provider.GetRequiredService<IServerTransport<ISingleStreamTransportConnection>>()));

        services.
            TryAddSingleton<IClientTransport<IMultiplexedTransportConnection>>(
                provider => new SlicClientTransport(
                    provider.GetRequiredService<IOptionsMonitor<SlicTransportOptions>>().Get("client"),
                    provider.GetRequiredService<IClientTransport<ISingleStreamTransportConnection>>()));

        services.TryAddSingleton(new MultiplexedTransportOptions());

        services.AddOptions<SlicTransportOptions>("client").Configure<MultiplexedTransportOptions>(
            ConfigureMultiplexedTransportOptions);
        services.AddOptions<SlicTransportOptions>("server").Configure<MultiplexedTransportOptions>(
            ConfigureMultiplexedTransportOptions);

        return services;

        static void ConfigureMultiplexedTransportOptions(
            SlicTransportOptions options,
            MultiplexedTransportOptions multiplexedTransportOptions)
        {
            if (multiplexedTransportOptions.BidirectionalStreamMaxCount is int bidirectionalStreamMaxCount)
            {
                options.BidirectionalStreamMaxCount = bidirectionalStreamMaxCount;
            }

            if (multiplexedTransportOptions.UnidirectionalStreamMaxCount is int unidirectionalStreamMaxCount)
            {
                options.UnidirectionalStreamMaxCount = unidirectionalStreamMaxCount;
            }

            if (multiplexedTransportOptions.IdleTimeout is TimeSpan idleTimeout)
            {
                options.IdleTimeout = idleTimeout;
            }
        }
    }
}
