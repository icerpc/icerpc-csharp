// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using IceRpc.Internal;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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

        services.AddOptions<MultiplexedClientConnectionOptions>().Configure(
            options => options.StreamErrorCodeConverter = IceRpcProtocol.Instance.MultiplexedStreamErrorCodeConverter);

        services.AddOptions<MultiplexedServerConnectionOptions>().Configure(
            options => options.StreamErrorCodeConverter = IceRpcProtocol.Instance.MultiplexedStreamErrorCodeConverter);

        services.AddOptions<MultiplexedListenerOptions>().Configure<IOptions<MultiplexedServerConnectionOptions>>(
            (options, serverConnectionOptions) => options.ServerConnectionOptions = serverConnectionOptions.Value);

        services.AddSingleton(provider =>
        {
            var serverTransport = provider.GetRequiredService<IMultiplexedServerTransport>();
            var listener = serverTransport.Listen(
                provider.GetRequiredService<IOptions<MultiplexedListenerOptions>>().Value with { Endpoint = endpoint });
            return listener;
        });

        services.AddSingleton(provider =>
        {
            var listener = provider.GetRequiredService<IMultiplexedListener>();
            var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
            var connection = clientTransport.CreateConnection(
                provider.GetRequiredService<IOptions<MultiplexedClientConnectionOptions>>().Value with
                {
                    Endpoint = listener.Endpoint
                });
            return (SlicMultiplexedConnection)connection;
        });
        return services;
    }
}
