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
            TryAddSingleton<IServerTransport<IMultiplexedConnection>>(
                provider => new SlicServerTransport(
                    slicTransportOptions ?? new SlicTransportOptions(),
                    provider.GetRequiredService<IServerTransport<IDuplexConnection>>()));

        services.
            TryAddSingleton<IClientTransport<IMultiplexedConnection>>(
                provider => new SlicClientTransport(
                    slicTransportOptions ?? new SlicTransportOptions(),
                    provider.GetRequiredService<IClientTransport<IDuplexConnection>>()));

        services.AddSingleton(provider =>
        {
            var serverTransport = provider.GetRequiredService<IServerTransport<IMultiplexedConnection>>();
            var listener = serverTransport.Listen(endpoint, null, NullLogger.Instance);
            return listener;
        });

        services.AddSingleton<SlicMultiplexedConnection>(provider =>
        {
            var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
            var clientTransport = provider.GetRequiredService<IClientTransport<IMultiplexedConnection>>();
            var connection = clientTransport.CreateConnection(listener.Endpoint, null, NullLogger.Instance);
            return (SlicMultiplexedConnection)connection;
        });
        return services;
    }
}
