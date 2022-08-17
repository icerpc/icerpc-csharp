// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

[Parallelizable(ParallelScope.All)]
public class SlicConformanceTests : MultiplexedTransportConformanceTests
{
    /// <summary>Creates the service collection used for Slic multiplexed transports for conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection()
            .AddColocTransport()
            .AddSingleton(provider =>
            {
                var transport = provider.GetRequiredService<IMultiplexedServerTransport>();
                return transport.Listen(
                    new ServerAddress(Protocol.IceRpc) { Host = "colochost" },
                    provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                    null);
            });

        services.
            TryAddSingleton<IMultiplexedServerTransport>(
                provider => new SlicServerTransport(
                    provider.GetRequiredService<IOptionsMonitor<SlicTransportOptions>>().Get("server"),
                    provider.GetRequiredService<IDuplexServerTransport>()));

        services.
            TryAddSingleton<IMultiplexedClientTransport>(
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
