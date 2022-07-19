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
        services.AddColocTransport().AddClientServerTest(dispatcher, new Endpoint(protocol) { Host = host });

    public static IServiceCollection AddColocTest(this IServiceCollection services, IDispatcher dispatcher) =>
        services.AddColocTest(dispatcher, Protocol.IceRpc);

    /// <summary>Installs coloc client-server test.</summary>
    public static IServiceCollection AddColocTest(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure,
        Protocol protocol,
        string host = "colochost") =>
        services.AddColocTransport().AddClientServerTest(configure, new Endpoint(protocol) { Host = host });

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
        services.AddClientServerTest(dispatcher, new Endpoint(protocol) { Host = "127.0.0.1", Port = 0 });

    public static ServiceCollection UseDuplexTransport(this ServiceCollection collection, Endpoint endpoint)
    {
        collection.AddSingleton(provider =>
        {
            ILogger logger = provider.GetService<ILogger>() ?? NullLogger.Instance;
            SslServerAuthenticationOptions? serverAuthenticationOptions =
                provider.GetService<IOptions<SslServerAuthenticationOptions>>()?.Value;
            IDuplexServerTransport serverTransport = provider.GetRequiredService<IDuplexServerTransport>();
            return serverTransport.Listen(endpoint, serverAuthenticationOptions, logger);
        });

        collection.AddSingleton(provider =>
        {
            ILogger logger = provider.GetService<ILogger>() ?? NullLogger.Instance;
            SslClientAuthenticationOptions? clientAuthenticationOptions =
                provider.GetService<IOptions<SslClientAuthenticationOptions>>()?.Value;
            IDuplexListener listener =
                provider.GetRequiredService<IDuplexListener>();
            IDuplexClientTransport clientTransport =
                provider.GetRequiredService<IDuplexClientTransport>();

            return clientTransport.CreateConnection(
                listener.Endpoint,
                clientAuthenticationOptions,
                logger);
        });
        return collection;
    }

    public static ServiceCollection UseDuplexTransport(this ServiceCollection collection, Uri endpointUri) =>
        collection.UseDuplexTransport(new Endpoint(endpointUri));

    /// <summary>Adds a Server and ClientConnection singletons, with the server listening on the specified endpoint and
    /// the client connection connecting to the server's endpoint.</summary>
    /// <remarks>When the endpoint's port is 0 and transport is not coloc, you need to create the server and call Listen
    /// on it before creating the client connection.</remarks>
    private static IServiceCollection AddClientServerTest(
        this IServiceCollection services,
        IDispatcher dispatcher,
        Endpoint endpoint)
    {
        services.AddSingleton<ILoggerFactory>(LogAttributeLoggerFactory.Instance);
        services.AddSingleton(LogAttributeLoggerFactory.Instance.Logger);

        services.AddOptions<ServerOptions>().Configure(options =>
        {
            options.ConnectionOptions.Dispatcher = dispatcher;
            options.Endpoint = endpoint;
        });
        services.AddIceRpcServer();

        services
            .AddOptions<ClientConnectionOptions>()
            .Configure<Server>((options, server) => options.Endpoint = server.Endpoint);

        services.AddIceRpcClientConnection();

        return services;
    }

    private static IServiceCollection AddClientServerTest(
        this IServiceCollection services,
        Action<IDispatcherBuilder> configure,
        Endpoint endpoint)
    {
        services.AddSingleton<ILoggerFactory>(LogAttributeLoggerFactory.Instance);
        services.AddSingleton(provider =>
        {
            ILoggerFactory factory = provider.GetRequiredService<ILoggerFactory>();
            return factory.CreateLogger("Test");
        });

        services.AddOptions<ServerOptions>().Configure(options => options.Endpoint = endpoint);
        services.AddIceRpcServer(configure);

        services
            .AddOptions<ClientConnectionOptions>()
            .Configure<Server>((options, server) => options.Endpoint = server.Endpoint);

        services.AddIceRpcClientConnection();

        return services;
    }
}
