// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net.Security;

namespace IceRpc.Tests;

public static class ServiceCollectionExtensions
{
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

    public static IServiceCollection AddTcpTest(
        this IServiceCollection services,
        IDispatcher dispatcher,
        Protocol protocol) =>
        services.AddClientServerTest(dispatcher, new Endpoint(protocol) { Host = "127.0.0.1", Port = 0 });

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

    /// <summary>Adds a Server and ClientConnection singletons, with the server listening on the specified endpoint and
    /// the client connection connecting to the server's endpoint.</summary>
    /// <remarks>When the endpoint's port is 0 and transport is not coloc, you need to create the server and call Listen
    /// on it before creating the client connection.</remarks>
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
}
