// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Slic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace IceRpc.Tests.Transports.Slic;

internal static class SlicTransportServiceCollectionExtensions
{
    internal static IServiceCollection AddSlicTest(this IServiceCollection services) =>
        services.AddMultiplexedTransportTest().AddSlicTransport();

    internal static IServiceCollection AddSlicTransport(this IServiceCollection serviceCollection)
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
                    provider.GetRequiredService<IDuplexClientTransport>()));

        services.AddOptions<SlicTransportOptions>("client");
        services.AddOptions<SlicTransportOptions>("server");

        return services;
    }
}
