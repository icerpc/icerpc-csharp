// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Conformance.Tests;
using IceRpc.Internal;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace IceRpc.Tests.Transports;

// Slic conformance tests are run with both TCP and Coloc. Running with both better exercises the Slic implementation
// and helps with detecting timing related bugs or race conditions.

public abstract class SlicConformanceTests : MultiplexedTransportConformanceTests
{
    /// <summary>Creates the service collection used for Slic multiplexed transports for conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection()
    {
        var services = new ServiceCollection();

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

[Parallelizable(ParallelScope.All)]
public class SlicOverTcpConformanceTests : SlicConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() =>
        base.CreateServiceCollection()
            .AddSingleton<IDuplexClientTransport>(provider => new TcpClientTransport())
            .AddSingleton<IDuplexServerTransport>(provider => new TcpServerTransport())
            .AddSingleton(provider =>
            {
                IMultiplexedServerTransport transport = provider.GetRequiredService<IMultiplexedServerTransport>();
                return transport.Listen(
                    new ServerAddress(Protocol.IceRpc) { Host = "127.0.0.1", Port = 0 },
                    provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                    null);
            });
}

[Parallelizable(ParallelScope.All)]
public class SlicOverColocConformanceTests : SlicConformanceTests
{
    protected override IServiceCollection CreateServiceCollection() =>
        base.CreateServiceCollection()
        .AddSingleton<ColocTransport>()
        .AddSingleton(provider => provider.GetRequiredService<ColocTransport>().ClientTransport)
        .AddSingleton(provider => provider.GetRequiredService<ColocTransport>().ServerTransport)
        .AddSingleton(provider =>
        {
            IMultiplexedServerTransport transport = provider.GetRequiredService<IMultiplexedServerTransport>();
            return transport.Listen(
                new ServerAddress(Protocol.IceRpc) { Host = "colochost" },
                provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                null);
        });
}
