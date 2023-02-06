// Copyright (c) ZeroC, Inc.

using IceRpc.Conformance.Tests;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Net.Security;

namespace IceRpc.Tests.Transports;

[Parallelizable(ParallelScope.All)]
public class SlicConnectionConformanceTests : MultiplexedConnectionConformanceTests
{
    /// <summary>Creates the service collection used for Slic connection conformance testing.</summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().UseSlic();
}

[Parallelizable(ParallelScope.All)]
public class SlicStreamConformanceTests : MultiplexedStreamConformanceTests
{
    /// <summary>Creates the service collection used for Slic stream conformance testing.
    /// </summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().UseSlic();
}

[Parallelizable(ParallelScope.All)]
public class SlicListenerConformanceTests : MultiplexedListenerConformanceTests
{
    /// <summary>Creates the service collection used for Slic listener conformance testing.
    /// </summary>
    protected override IServiceCollection CreateServiceCollection() => new ServiceCollection().UseSlic();
}

internal static class SlicTransportConformanceTestsServiceCollection
{
    internal static IServiceCollection UseSlic(this IServiceCollection serviceCollection)
    {
        IServiceCollection services = serviceCollection
            .AddColocTransport()
            .AddSingleton<IMultiplexedServerTransport>(
                provider => new SlicServerTransport(
                    provider.GetRequiredService<IOptionsMonitor<SlicTransportOptions>>().Get("server"),
                    provider.GetRequiredService<IDuplexServerTransport>()))
            .AddSingleton<IMultiplexedClientTransport>(
                provider => new SlicClientTransport(
                    provider.GetRequiredService<IOptionsMonitor<SlicTransportOptions>>().Get("client"),
                    provider.GetRequiredService<IDuplexClientTransport>()))
            .AddSingleton(provider =>
                provider.GetRequiredService<IMultiplexedServerTransport>().Listen(
                    new ServerAddress(Protocol.IceRpc) { Host = "colochost" },
                    provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                    serverAuthenticationOptions: provider.GetService<SslServerAuthenticationOptions>()));

        services.AddOptions<SlicTransportOptions>("client");
        services.AddOptions<SlicTransportOptions>("server");

        return services;
    }
}
