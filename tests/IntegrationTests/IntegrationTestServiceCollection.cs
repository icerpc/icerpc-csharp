// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Tests;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.IntegrationTests;

public class IntegrationTestServiceCollection : ServiceCollection
{
    public IntegrationTestServiceCollection()
    {
        this.UseColoc();

        this.UseSlic();

        this.AddScoped(provider => new ClientConnectionOptions
        {
            Dispatcher = provider.GetService<IDispatcher>() ?? ConnectionOptions.DefaultDispatcher,
            RemoteEndpoint = provider.GetService<Server>()?.Endpoint,
        });

        this.AddScoped(
            provider => new ConnectionPool(
                provider.GetService<ConnectionPoolOptions>() ?? new ConnectionPoolOptions(),
                provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
                provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

        this.AddScoped(provider =>
        {
            var serverOptions = provider.GetService<ServerOptions>() ?? new ServerOptions();
            serverOptions.ConnectionOptions.Dispatcher = provider.GetRequiredService<IDispatcher>();
            serverOptions.Endpoint = provider.GetRequiredService<Endpoint>();

            var server = new Server(
                serverOptions,
                provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
                provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>(),
                provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>());
            server.Listen();
            return server;
        });

        this.AddScoped(provider => new ClientConnection(
            provider.GetRequiredService<ClientConnectionOptions>(),
            provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
            provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
            provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));
    }
}
