// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Tests;

public class SliceTestServiceCollection : ServiceCollection
{
    public SliceTestServiceCollection()
    {
        this.UseColoc();

        this.UseSlic();

        this.TryAddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);

        this.AddScoped(provider =>
        {
            var serverOptions = provider.GetService<ServerOptions>() ?? new ServerOptions();
            serverOptions.Endpoint = provider.GetRequiredService<Endpoint>();
            serverOptions.ConnectionOptions = new ConnectionOptions()
            {
                Dispatcher = provider.GetRequiredService<IDispatcher>()
            };
            var server = new Server(
                serverOptions,
                provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
                provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>(),
                provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>());
            server.Listen();
            return server;
        });

        this.AddScoped(provider =>
        {
            var connectionOptions = provider.GetService<ClientConnectionOptions>() ?? new ClientConnectionOptions();
            connectionOptions.Dispatcher ??= provider.GetRequiredService<IDispatcher>();
            connectionOptions.RemoteEndpoint = provider.GetRequiredService<Server>().Endpoint;
            return new ClientConnection(
                connectionOptions,
                provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance,
                provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>());
        });
    }
}
