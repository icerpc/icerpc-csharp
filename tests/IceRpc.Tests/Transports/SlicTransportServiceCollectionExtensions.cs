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
            TryAddSingleton<IServerTransport<IMultiplexedTransportConnection>>(
                provider => new SlicServerTransport(
                    slicTransportOptions ?? new SlicTransportOptions(),
                    provider.GetRequiredService<IServerTransport<ISingleStreamTransportConnection>>()));

        services.
            TryAddSingleton<IClientTransport<IMultiplexedTransportConnection>>(
                provider => new SlicClientTransport(
                    slicTransportOptions ?? new SlicTransportOptions(),
                    provider.GetRequiredService<IClientTransport<ISingleStreamTransportConnection>>()));

        services.AddSingleton(provider =>
        {
            var serverTransport = provider.GetRequiredService<IServerTransport<IMultiplexedTransportConnection>>();
            var listener = serverTransport.Listen(endpoint, null, NullLogger.Instance);
            return listener;
        });

        services.AddSingleton<SlicTransportConnection>(provider =>
        {
            var listener = provider.GetRequiredService<IListener<IMultiplexedTransportConnection>>();
            var clientTransport = provider.GetRequiredService<IClientTransport<IMultiplexedTransportConnection>>();
            var connection = clientTransport.CreateConnection(listener.Endpoint, null, NullLogger.Instance);
            return (SlicTransportConnection)connection;
        });
        return services;
    }
}
