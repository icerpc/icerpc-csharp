// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.Tests;

public class SliceTestServiceCollection : ServiceCollection
{
    public SliceTestServiceCollection()
    {
        this.UseColoc();

        this.UseSlic();

        this.AddScoped(provider =>
        {
            var serverOptions = provider.GetService<ServerOptions>() ?? new ServerOptions();
            serverOptions.IceRpcServerOptions = new IceRpcServerOptions
            {
                ServerTransport =
                    provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>()
            };
            serverOptions.Dispatcher = provider.GetRequiredService<IDispatcher>();
            serverOptions.Endpoint = provider.GetRequiredService<Endpoint>();
            serverOptions.LoggerFactory = provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
            var server = new Server(serverOptions);
            server.Listen();
            return server;
        });

        this.AddScoped(provider =>
        {
            var connectionOptions = provider.GetService<ConnectionOptions>() ?? new ConnectionOptions();
            connectionOptions.IceRpcClientOptions = new IceRpcClientOptions
            {
                ClientTransport = provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>()
            };
            connectionOptions.Dispatcher ??= provider.GetRequiredService<IDispatcher>();
            connectionOptions.RemoteEndpoint = provider.GetRequiredService<Server>().Endpoint;
            connectionOptions.LoggerFactory = provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
            return new Connection(connectionOptions);
        });
    }
}
