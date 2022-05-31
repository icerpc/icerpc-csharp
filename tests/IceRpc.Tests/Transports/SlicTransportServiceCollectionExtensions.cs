// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Tests.Transports;

public static class SlicTransportServiceCollectionExtensions
{
    public static IServiceCollection AddSlicTest(
        this IServiceCollection services,
        SlicTransportOptions? slicTransportOptions = null)
    {
        services.AddColocTransport();
        var endpoint = new Endpoint(Protocol.IceRpc) { Host = "colochost" };

        services.
            TryAddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicServerTransport(
                    slicTransportOptions ?? new SlicTransportOptions(),
                    provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));

        services.
            TryAddSingleton<IClientTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicClientTransport(
                    slicTransportOptions ?? new SlicTransportOptions(),
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
