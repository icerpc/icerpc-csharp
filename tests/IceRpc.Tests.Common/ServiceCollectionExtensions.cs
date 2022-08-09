// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Builder;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Security;

namespace IceRpc.Tests.Common;

public static class ServiceCollectionExtensions
{
    /// <summary>Installs coloc client-server test.</summary>
    public static IServiceCollection AddColocTest(
        this IServiceCollection services,
        IDispatcher dispatcher,
        Protocol protocol,
        string host = "colochost") =>
        services.AddColocTransport().AddClientServerTest(dispatcher, new ServerAddress(protocol) { Host = host });

    public static IServiceCollection AddColocTest(this IServiceCollection services, IDispatcher dispatcher) =>
        services.AddColocTest(dispatcher, Protocol.IceRpc);

    /// <summary>Installs coloc client-server test.</summary>
    public static IServiceCollection AddColocTest(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure,
        Protocol protocol,
        string host = "colochost") =>
        services.AddColocTransport().AddClientServerTest(configure, new ServerAddress(protocol) { Host = host });

    public static IServiceCollection AddColocTest(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure) =>
        services.AddColocTest(configure, Protocol.IceRpc);

    /// <summary>Installs the coloc duplex transport.</summary>
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
        services.AddClientServerTest(dispatcher, new ServerAddress(protocol) { Host = "127.0.0.1", Port = 0 });

    public static ServiceCollection UseDuplexTransport(this ServiceCollection collection, ServerAddress serverAddress)
    {
        collection.AddSingleton(provider =>
        {
            DuplexConnectionOptions? connectionOptions =
                provider.GetService<DuplexConnectionOptions>();
            SslServerAuthenticationOptions? serverAuthenticationOptions =
                provider.GetService<IOptions<SslServerAuthenticationOptions>>()?.Value;
            IDuplexServerTransport serverTransport = provider.GetRequiredService<IDuplexServerTransport>();
            return serverTransport.Listen(
                serverAddress,
                connectionOptions ?? new DuplexConnectionOptions(),
                serverAuthenticationOptions);
        });

        collection.AddSingleton(provider =>
        {
            DuplexConnectionOptions? connectionOptions =
                provider.GetService<DuplexConnectionOptions>();
            SslClientAuthenticationOptions? clientAuthenticationOptions =
                provider.GetService<IOptions<SslClientAuthenticationOptions>>()?.Value;
            IDuplexListener listener = provider.GetRequiredService<IDuplexListener>();
            IDuplexClientTransport clientTransport = provider.GetRequiredService<IDuplexClientTransport>();

            return clientTransport.CreateConnection(
                listener.ServerAddress,
                connectionOptions ?? new DuplexConnectionOptions(),
                clientAuthenticationOptions);
        });
        return collection;
    }

    public static ServiceCollection UseDuplexTransport(this ServiceCollection collection, Uri serverAddressUri) =>
        collection.UseDuplexTransport(new ServerAddress(serverAddressUri));

    /// <summary>Adds a Server and ClientConnection singletons, with the server listening on the specified server address and
    /// the client connection connecting to the server's server address.</summary>
    /// <remarks>When the server address port is 0 and transport is not coloc, you need to create the server and call Listen
    /// on it before creating the client connection.</remarks>
    private static IServiceCollection AddClientServerTest(
        this IServiceCollection services,
        IDispatcher dispatcher,
        ServerAddress serverAddress)
    {
        services.AddSingleton<ILoggerFactory>(LogAttributeLoggerFactory.Instance);
        services.AddSingleton(LogAttributeLoggerFactory.Instance.Logger);

        services.AddOptions<ServerOptions>().Configure(options =>
        {
            options.ConnectionOptions.Dispatcher = dispatcher;
            options.ServerAddress = serverAddress;
        });
        services.AddIceRpcServer();

        services
            .AddOptions<ClientConnectionOptions>()
            .Configure<Server>((options, server) => options.ServerAddress = server.ServerAddress);

        services.AddIceRpcClientConnection();

        return services;
    }

    private static IServiceCollection AddClientServerTest(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure,
        ServerAddress serverAddress)
    {
        services.AddSingleton<ILoggerFactory>(LogAttributeLoggerFactory.Instance);
        services.AddSingleton(provider =>
        {
            ILoggerFactory factory = provider.GetRequiredService<ILoggerFactory>();
            return factory.CreateLogger("Test");
        });

        services.AddOptions<ServerOptions>().Configure(options => options.ServerAddress = serverAddress);
        services.AddIceRpcServer(configure);

        services
            .AddOptions<ClientConnectionOptions>()
            .Configure<Server>((options, server) => options.ServerAddress = server.ServerAddress);

        services.AddIceRpcClientConnection();

        return services;
    }
}
