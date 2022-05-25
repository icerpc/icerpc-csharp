// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
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

        services.AddSlicTransport();

        services.AddSingleton(provider =>
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
            .AddSingleton(provider =>
                new ClientConnection(
                    provider.GetRequiredService<IOptions<ClientConnectionOptions>>().Value,
                    loggerFactory: provider.GetService<ILoggerFactory>(),
                    provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

    public static IServiceCollection AddIceRpcConnectionPool(this IServiceCollection services) =>
        services
            .AddIceRpcClient()
            .AddSingleton(provider =>
                new ConnectionPool(
                    provider.GetRequiredService<IOptions<ConnectionPoolOptions>>().Value,
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

        services.
            TryAddSingleton<IClientTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicClientTransport(
                    provider.GetRequiredService<IOptions<SlicTransportOptions>>().Value,
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

        return services;
    }

    /// <summary>Installs coloc client-server test.</summary>
    public static IServiceCollection AddColocTest(
        this IServiceCollection services,
        IDispatcher dispatcher,
        Protocol protocol,
        string host = "colochost") =>
        services.AddColocTransport().AddClientServerTest(dispatcher, new Endpoint(protocol) { Host = host });

    public static IServiceCollection AddColocTest(this IServiceCollection services, IDispatcher dispatcher) =>
        services.AddColocTest(dispatcher, Protocol.IceRpc);

    /// <summary>Installs the coloc simple transport.</summary>
    public static IServiceCollection AddColocTransport(this IServiceCollection services)
    {
        services.TryAddSingleton<ColocTransport>();
        return services
            .AddSingleton(provider => provider.GetRequiredService<ColocTransport>().ClientTransport)
            .AddSingleton(provider => provider.GetRequiredService<ColocTransport>().ServerTransport);
    }

    public static IServiceCollection AddSlicTransport(this IServiceCollection services)
    {
        services.AddOptions<SlicTransportOptions>();

        services.
            TryAddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicServerTransport(
                    provider.GetRequiredService<IOptions<SlicTransportOptions>>().Value,
                    provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));

        services.
            TryAddSingleton<IClientTransport<IMultiplexedNetworkConnection>>(
                provider => new SlicClientTransport(
                    provider.GetRequiredService<IOptions<SlicTransportOptions>>().Value,
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));
        return services;
    }
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
