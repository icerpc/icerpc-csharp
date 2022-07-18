// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
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
            TryAddSingleton<IMultiplexedServerTransport>(
                provider => new SlicServerTransport(
                    slicTransportOptions ?? new SlicTransportOptions(),
                    provider.GetRequiredService<IDuplexServerTransport>()));

        services.
            TryAddSingleton<IMultiplexedClientTransport>(
                provider => new SlicClientTransport(
                    slicTransportOptions ?? new SlicTransportOptions(),
                    provider.GetRequiredService<IDuplexClientTransport>()));

        services.AddSingleton(provider =>
        {
            var serverTransport = provider.GetRequiredService<IMultiplexedServerTransport>();
            var listener = serverTransport.Listen(endpoint, null, NullLogger.Instance);
            return listener;
        });

        services.AddSingleton<SlicMultiplexedConnection>(provider =>
        {
            var listener = provider.GetRequiredService<IMultiplexedListener>();
            var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
            var connection = clientTransport.CreateConnection(listener.Endpoint, null, NullLogger.Instance);
            return (SlicMultiplexedConnection)connection;
        });
        return services;
    }
}
