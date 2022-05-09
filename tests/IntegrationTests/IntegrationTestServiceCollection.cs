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

        this.AddScoped(provider => new ConnectionOptions
        {
            IceClientOptions = new()
            {
                ClientTransport = provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()
            },
            IceRpcClientOptions = new()
            {
                ClientTransport = provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>()
            },
            Dispatcher = provider.GetService<IDispatcher>() ?? ConnectionOptions.DefaultDispatcher,
            RemoteEndpoint = provider.GetService<Server>()?.Endpoint,
            LoggerFactory = provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
        });

        this.AddScoped(provider =>
        {
            ConnectionOptions connectionOptions = provider.GetRequiredService<ConnectionOptions>();
            return new ConnectionPool(new ConnectionOptions
            {
                IceClientOptions = connectionOptions.IceClientOptions,
                IceRpcClientOptions = connectionOptions.IceRpcClientOptions,
                Dispatcher = connectionOptions.Dispatcher,
                // RemoteEndpoint must remain null
                LoggerFactory = connectionOptions.LoggerFactory
            });
        });

        this.AddScoped(provider =>
        {
            var serverOptions = provider.GetService<ServerOptions>() ?? new ServerOptions();
            serverOptions.IceServerOptions = new()
            {
                ServerTransport = provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()
            };
            serverOptions.IceRpcServerOptions = new()
            {
                ServerTransport = provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>()
            };
            serverOptions.Dispatcher = provider.GetRequiredService<IDispatcher>();
            serverOptions.Endpoint = provider.GetRequiredService<Endpoint>();
            serverOptions.LoggerFactory = provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
            var server = new Server(serverOptions);
            server.Listen();
            return server;
        });

        this.AddScoped(provider => new Connection(provider.GetRequiredService<ConnectionOptions>()));
    }
}
