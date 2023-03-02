// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
        services
            .AddColocTransport()
            .AddSingleton<ILoggerFactory>(LogAttributeLoggerFactory.Instance)
            .AddSingleton(LogAttributeLoggerFactory.Instance.Logger)
            .AddIceRpcClientConnection();

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

    /// <summary>Adds Listener and IDuplexConnection singletons, with the listener listening on the specified server
    /// address and the client connection connecting to the listener's server address.</summary>
    public static IServiceCollection AddDuplexTransportClientServerTest(
        this IServiceCollection services,
        Uri serverAddressUri) => services
            .AddSingleton(provider =>
                provider.GetRequiredService<IDuplexServerTransport>().Listen(
                    new ServerAddress(serverAddressUri),
                    provider.GetService<IOptions<DuplexConnectionOptions>>()?.Value ?? new(),
                    provider.GetService<SslServerAuthenticationOptions>()))
            .AddSingleton(provider =>
                provider.GetRequiredService<IDuplexClientTransport>().CreateConnection(
                    provider.GetRequiredService<IListener<IDuplexConnection>>().ServerAddress,
                    provider.GetService<IOptions<DuplexConnectionOptions>>()?.Value ?? new(),
                    provider.GetService<SslClientAuthenticationOptions>()));

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
}
