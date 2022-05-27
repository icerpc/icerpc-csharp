// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Transports.Tests;

public static class SlicTransportServiceCollectionExtensions
{
    public static IServiceCollection AddSlicTest(
        this IServiceCollection services,
        SlicTransportOptions slicTransportOptions,
        SlicTransportOptions? clientSlicTransportOptions = null)
    {
        clientSlicTransportOptions ??= slicTransportOptions;

        services.AddColocTransport();
        var endpoint = new Endpoint(Protocol.IceRpc) { Host = "colochost" };

        services.
            AddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicServerTransport(
                    slicTransportOptions,
                    provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));

        services.
            AddSingleton<IClientTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicClientTransport(
                    clientSlicTransportOptions,
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

        services.AddSingleton(provider =>
        {
            var serverTransport = provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();
            var listener = serverTransport.Listen(endpoint, null, NullLogger.Instance);
            return listener;
        });
        return services;
    }
}
