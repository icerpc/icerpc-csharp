// Copyright (c) ZeroC, Inc.

using IceRpc.Extensions.DependencyInjection;
using IceRpc.Transports;
using IceRpc.Transports.Coloc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net.Security;

namespace IceRpc.Tests.Common;

/// <summary>Extension methods for setting up IceRPC tests in an <see cref="IServiceCollection" />.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Adds Server and ClientConnection singletons, with the server listening on the specified host.</summary>
    public static IServiceCollection AddClientServerColocTest(
        this IServiceCollection services,
        Protocol? protocol = null,
        IDispatcher? dispatcher = null,
        string? host = null)
    {
        protocol ??= Protocol.IceRpc;
        host ??= "colochost";

        var serverAddress = new ServerAddress(protocol) { Host = host };

        // Note: the multiplexed transport is added by IceRpcServer/IceRpcClientConnection.
        services.AddColocTransport().AddIceRpcClientConnection();

        services.AddOptions<ServerOptions>().Configure(
            options =>
            {
                options.ServerAddress = serverAddress;
                options.ConnectionOptions.Dispatcher = dispatcher;
            });
        services.AddOptions<ClientConnectionOptions>().Configure(options => options.ServerAddress = serverAddress);
        services.AddIceRpcServer();
        return services;
    }

    /// <summary>Installs the coloc duplex transport.</summary>
    public static IServiceCollection AddColocTransport(this IServiceCollection services) => services
        .AddSingleton<ColocTransportOptions>()
        .AddSingleton(provider => new ColocTransport(provider.GetRequiredService<ColocTransportOptions>()))
        .AddSingleton(provider => provider.GetRequiredService<ColocTransport>().ClientTransport)
        .AddSingleton(provider => provider.GetRequiredService<ColocTransport>().ServerTransport);

    /// <summary>Adds Listener and ClientServerDuplexConnection singletons, with the listener listening on the
    /// specified server address.</summary>
    public static IServiceCollection AddDuplexTransportTest(
        this IServiceCollection services,
        Uri? serverAddressUri = null) => services
            .AddSingleton(provider =>
                provider.GetRequiredService<IDuplexServerTransport>().Listen(
                    new ServerAddress(serverAddressUri ?? new Uri("icerpc://colochost")),
                    provider.GetService<IOptions<DuplexConnectionOptions>>()?.Value ?? new(),
                    serverAuthenticationOptions: provider.GetService<SslServerAuthenticationOptions>()))
            .AddSingleton(provider =>
            {
                var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
                var clientTransport = provider.GetRequiredService<IDuplexClientTransport>();
                var connection = clientTransport.CreateConnection(
                    listener.ServerAddress,
                    provider.GetService<IOptions<DuplexConnectionOptions>>()?.Value ?? new(),
                    provider.GetService<SslClientAuthenticationOptions>());
                return new ClientServerDuplexConnection(connection, listener);
            });

    /// <summary>Adds Listener and ClientServerMultiplexedConnection singletons, with the listener listening on the
    /// specified server address.</summary>
    public static IServiceCollection AddMultiplexedTransportTest(
        this IServiceCollection services,
        Uri? serverAddressUri = null) => services
            .AddSingleton(provider =>
                provider.GetRequiredService<IMultiplexedServerTransport>().Listen(
                    new ServerAddress(serverAddressUri ?? new Uri("icerpc://colochost")),
                    provider.GetService<IOptions<MultiplexedConnectionOptions>>()?.Value ?? new(),
                    serverAuthenticationOptions: provider.GetService<SslServerAuthenticationOptions>()))
            .AddSingleton(provider =>
            {
                var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
                var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
                var connection = clientTransport.CreateConnection(
                    listener.ServerAddress,
                    provider.GetService<IOptions<MultiplexedConnectionOptions>>()?.Value ?? new(),
                    provider.GetService<SslClientAuthenticationOptions>());
                return new ClientServerMultiplexedConnection(connection, listener);
            });

    /// <summary>Installs a transport decorator for the last registered transport.</summary>
    internal static void AddSingletonTransportDecorator<TTransportService, TTransportDecoratorService>(
        this IServiceCollection services,
        Func<TTransportService, TTransportDecoratorService> decorateFunc)
        where TTransportService : class where TTransportDecoratorService : class, TTransportService
    {
        // Find the last TTransportService transport service registered with this service collection.
        ServiceDescriptor? descriptor = services.LastOrDefault(
            desc => desc!.ServiceType == typeof(TTransportService),
            null);
        if (descriptor is null)
        {
            throw new ArgumentException($"No {typeof(TTransportService)} service is registered");
        }

        Func<IServiceProvider, object>? factory = descriptor.ImplementationFactory;
        if (factory is null)
        {
            throw new ArgumentException(
                "Only transport services registered with an implementation factory are supported.");
        }
        if (descriptor.Lifetime != ServiceLifetime.Singleton)
        {
            throw new NotSupportedException(
                "Only transport services registered with the singleton lifetime are supported.");
        }

        // Register the transport service decorator implementation.
        _ = services.AddSingleton(provider => decorateFunc((TTransportService)factory(provider)));

        // Register the transport service interface.
        services.AddSingleton<TTransportService>(provider => provider.GetRequiredService<TTransportDecoratorService>());
    }
}
