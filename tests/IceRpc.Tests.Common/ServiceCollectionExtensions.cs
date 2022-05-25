// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using IceRpc.Transports.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Security;

namespace IceRpc.Tests;

public static class ServiceCollectionExtensions
{
    // TODO: move all AddIceRpc extension methods to IceRpc.Extensions.DependencyInjection.dll

    /// <summary>Adds IceRpc.Server to the service collection.</summary>
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services)
    {
        // the main server-side extension method

        services
            .AddOptions()
            .TryAddSingleton<IServerTransport<ISimpleNetworkConnection>>(
                provider => new TcpServerTransport(
                    provider.GetRequiredService<IOptions<TcpServerTransportOptions>>().Value));

        // TODO: fix SlicServerTransportOptions to extract the simple server transport
        services
            .AddOptions<SlicServerTransportOptions>()
            .Configure<IServerTransport<ISimpleNetworkConnection>>(
                (options, simpleServerTransport) => options.SimpleServerTransport = simpleServerTransport);

        services.
            TryAddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicServerTransport(
                    provider.GetRequiredService<IOptions<SlicServerTransportOptions>>().Value));

        services.AddSingleton<Server>(provider =>
            new Server(
                provider.GetRequiredService<IOptions<ServerOptions>>().Value,
                loggerFactory: provider.GetService<ILoggerFactory>(),
                provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>(),
                provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));

        return services;
    }

    /// <summary>Adds IceRpc.Server to the service collection and uses the specified dispatcher.</summary>
    public static IServiceCollection AddIceRpcServer(this IServiceCollection services, IDispatcher dispatcher)
    {
        services.AddOptions<ServerOptions>().Configure(options => options.ConnectionOptions.Dispatcher = dispatcher);
        return services.AddIceRpcServer();
    }

    public static IServiceCollection AddIceRpcClientConnection(this IServiceCollection services) =>
        services
            .AddIceRpcClient()
            // TODO: should this be IClientConnection?
            .AddSingleton<ClientConnection>(provider =>
                new ClientConnection(
                    provider.GetRequiredService<IOptions<ClientConnectionOptions>>().Value,
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

    private static IServiceCollection AddIceRpcClient(this IServiceCollection services)
    {
        // the main client-side extension method

        services
            .AddOptions()
            .TryAddSingleton<IClientTransport<ISimpleNetworkConnection>>(
                provider => new TcpClientTransport(
                    provider.GetRequiredService<IOptions<TcpClientTransportOptions>>().Value));

        // TODO: fix SlicClientransportOptions to extract the simple client transport
        services
            .AddOptions<SlicClientTransportOptions>()
            .Configure<IClientTransport<ISimpleNetworkConnection>>(
                (options, simpleClientTransport) => options.SimpleClientTransport = simpleClientTransport);

        services.
            TryAddSingleton<IClientTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicClientTransport(
                    provider.GetRequiredService<IOptions<SlicClientTransportOptions>>().Value));

        return services;
    }

    /// <summary>Installs coloc client-server test.</summary>
    public static IServiceCollection AddColocTest(
        this IServiceCollection services,
        IDispatcher dispatcher,
        Protocol protocol) =>
        services
            .AddSingleton<ColocTransport>()
            .AddSingleton(provider => provider.GetRequiredService<ColocTransport>().ClientTransport)
            .AddSingleton(provider => provider.GetRequiredService<ColocTransport>().ServerTransport)
            .AddClientServerTest(dispatcher, new Endpoint(protocol) { Host = "colochost" });

    public static IServiceCollection AddColocTest(this IServiceCollection services, IDispatcher dispatcher) =>
        services.AddColocTest(dispatcher, Protocol.IceRpc);

    public static IServiceCollection AddTcpTest(
        this IServiceCollection services,
        IDispatcher dispatcher,
        Protocol protocol) =>
        services.AddClientServerTest(dispatcher, new Endpoint(protocol) { Host = "127.0.0.1", Port = 0 });

    /// <summary>Adds a Server and ClientConnection singletons, with the server listening on the specified endpoint and
    /// the client connection connecting to the server's endpoint.</summary>
    /// <remarks>When the endpoint's port is 0 and transport is not coloc, you need to create the server and call Listen
    ///  on it before creating the client connection.</remarks>
    private static IServiceCollection AddClientServerTest(
        this IServiceCollection services,
        IDispatcher dispatcher,
        Endpoint endpoint)
    {
        services.AddOptions<ServerOptions>().Configure(options =>
        {
            options.ConnectionOptions.Dispatcher = dispatcher;
            options.Endpoint = endpoint;
        });
        services.AddIceRpcServer();

        services
            .AddOptions<ClientConnectionOptions>()
            .Configure<Server>((options, server) => options.RemoteEndpoint = server.Endpoint);

        services.AddIceRpcClientConnection();

        return services;
    }

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
                provider.GetRequiredService<IListener<ISimpleNetworkConnection>>();
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
