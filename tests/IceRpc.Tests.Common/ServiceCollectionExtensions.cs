// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using IceRpc.Transports.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;

namespace IceRpc.Tests;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection UseColoc(this IServiceCollection collection) =>
        collection.UseColoc(new ColocTransport());

    public static IServiceCollection UseColoc(this IServiceCollection collection, ColocTransport coloc)
    {
        collection.AddScoped(_ => coloc.ServerTransport);
        collection.AddScoped(_ => coloc.ClientTransport);
        collection.AddScoped(
            typeof(Endpoint),
            provider =>
            {
                string protocol = provider.GetService<Protocol>()?.Name ?? "icerpc";
                return Endpoint.FromString($"{protocol}://{Guid.NewGuid()}/");
            });
        return collection;
    }

    public static IServiceCollection UseDispatcher(this IServiceCollection collection, IDispatcher dispatcher) =>
        collection.AddScoped(_ => dispatcher);

    public static IServiceCollection UseProtocol(this IServiceCollection collection, string protocol) =>
        collection.AddScoped(_ => Protocol.FromString(protocol));

    public static IServiceCollection UseSlic(this IServiceCollection collection)
    {
        collection.AddScoped<IServerTransport<IMultiplexedNetworkConnection>>(provider =>
        {
            var simpleServerTransport = provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();
            var serverOptions = provider.GetService<SlicServerTransportOptions>() ?? new SlicServerTransportOptions();
            var multiplexedTransportOptions = provider.GetService<MultiplexedTransportOptions>();
            if (multiplexedTransportOptions?.BidirectionalStreamMaxCount is int bidirectionalStreamMaxCount)
            {
                serverOptions.BidirectionalStreamMaxCount = bidirectionalStreamMaxCount;
            }
            if (multiplexedTransportOptions?.UnidirectionalStreamMaxCount is int unidirectionalStreamMaxCount)
            {
                serverOptions.UnidirectionalStreamMaxCount = unidirectionalStreamMaxCount;
            }
            serverOptions.SimpleServerTransport = simpleServerTransport;
            return new SlicServerTransport(serverOptions);
        });

        collection.AddScoped<IClientTransport<IMultiplexedNetworkConnection>>(provider =>
        {
            var simpleClientTransport = provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();
            var clientOptions = provider.GetService<SlicClientTransportOptions>() ?? new SlicClientTransportOptions();
            var multiplexedTransportOptions = provider.GetService<MultiplexedTransportOptions>();
            if (multiplexedTransportOptions?.BidirectionalStreamMaxCount is int bidirectionalStreamMaxCount)
            {
                clientOptions.BidirectionalStreamMaxCount = bidirectionalStreamMaxCount;
            }
            if (multiplexedTransportOptions?.UnidirectionalStreamMaxCount is int unidirectionalStreamMaxCount)
            {
                clientOptions.UnidirectionalStreamMaxCount = unidirectionalStreamMaxCount;
            }
            clientOptions.SimpleClientTransport = simpleClientTransport;
            return new SlicClientTransport(clientOptions);
        });

        collection.AddScoped<IListener<IMultiplexedNetworkConnection>>(provider =>
        {
            var serverTransport = provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();
            return serverTransport.Listen(
                (Endpoint)provider.GetRequiredService(typeof(Endpoint)),
                null,
                NullLogger.Instance);
        });
        return collection;
    }

    public static ServiceCollection UseSimpleTransport(this ServiceCollection collection)
    {
        collection.AddScoped(provider =>
        {
            SslServerAuthenticationOptions? serverAuthenticationOptions =
                provider.GetService<SslServerAuthenticationOptions>();
            IServerTransport<ISimpleNetworkConnection>? serverTransport =
                provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();
            return serverTransport.Listen(
                provider.GetRequiredService<Endpoint>(),
                serverAuthenticationOptions,
                NullLogger.Instance);
        });

        collection.AddScoped(provider =>
        {
            SslClientAuthenticationOptions? clientAuthenticationOptions =
                provider.GetService<SslClientAuthenticationOptions>();
            IListener<ISimpleNetworkConnection> listener =
                provider.GetRequiredService<IListener<ISimpleNetworkConnection>>();;
            IClientTransport<ISimpleNetworkConnection> clientTransport =
                provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();
            return clientTransport.CreateConnection(
                listener.Endpoint,
                clientAuthenticationOptions,
                NullLogger.Instance);
        });
        return collection;
    }

    public static ServiceCollection UseTcp(
        this ServiceCollection collection,
        TcpServerTransportOptions? serverTransportOptions = null,
        TcpClientTransportOptions? clientTransportOptions = null)
    {
        collection.AddScoped<IServerTransport<ISimpleNetworkConnection>>(
            provider => new TcpServerTransport(
                serverTransportOptions ??
                provider.GetService<TcpServerTransportOptions>() ??
                new TcpServerTransportOptions()));

        collection.AddScoped<IClientTransport<ISimpleNetworkConnection>>(
            provider => new TcpClientTransport(
                clientTransportOptions ??
                provider.GetService<TcpClientTransportOptions>() ??
                new TcpClientTransportOptions()));

        collection.AddScoped(
            typeof(Endpoint),
            provider =>
            {
                string protocol = provider.GetService<Protocol>()?.Name ?? "icerpc";
                return Endpoint.FromString($"{protocol}://127.0.0.1:0/");
            });

        return collection;
    }

    public static IServiceCollection UseServerOptions(this IServiceCollection collection, ServerOptions options) =>
        collection.AddSingleton(options);
}
