// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IceRpc.IntegrationTests;

public class IntegrationTestServiceCollection : ServiceCollection
{
    public IntegrationTestServiceCollection()
    {
        this.AddScoped(_ => new ColocTransport());

        this.AddScoped(provider => provider.GetRequiredService<ColocTransport>().ServerTransport);
        this.AddScoped<IServerTransport<IMultiplexedNetworkConnection>>(
            provider => new SlicServerTransport(provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));

        this.AddScoped(provider => provider.GetRequiredService<ColocTransport>().ClientTransport);
        this.AddScoped<IClientTransport<IMultiplexedNetworkConnection>>(
            provider => new SlicClientTransport(provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

        this.AddScoped(
            typeof(Endpoint),
            provider => Endpoint.FromString($"{provider.GetRequiredService<Protocol>().Name}://{Guid.NewGuid()}"));

        this.AddScoped(provider => new ConnectionPool(provider.GetService<ConnectionOptions>() ?? new ConnectionOptions()));

        this.AddScoped(provider =>
        {
            var serverOptions = provider.GetService<ServerOptions>() ?? new ServerOptions();
            serverOptions.SimpleServerTransport = provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();
            serverOptions.MultiplexedServerTransport =
                provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();
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
            connectionOptions.SimpleClientTransport = provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();
            connectionOptions.MultiplexedClientTransport =
                provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>();
            connectionOptions.Dispatcher ??= provider.GetRequiredService<IDispatcher>();
            connectionOptions.RemoteEndpoint = provider.GetRequiredService<Server>().Endpoint;
            connectionOptions.LoggerFactory = provider.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
            return new Connection(connectionOptions);
        });
    }
}

public static class IntegrationTestServiceCollectionExtensions
{
    public static IServiceCollection UseDispatcher(this IServiceCollection collection, IDispatcher dispatcher) =>
        collection.AddScoped(_ => dispatcher);

    public static IServiceCollection UseProtocol(this IServiceCollection collection, string protocol) =>
        collection.AddScoped(_ => Protocol.FromString(protocol));

    public static IServiceCollection UseTcp(this IServiceCollection collection)
    {
        collection.AddScoped(
            typeof(Endpoint),
            provider => Endpoint.FromString($"{provider.GetRequiredService<Protocol>().Name}://127.0.0.1:0"));

        collection.AddScoped<IServerTransport<ISimpleNetworkConnection>>(_ => new TcpServerTransport());
        collection.AddScoped<IClientTransport<ISimpleNetworkConnection>>(_ => new TcpClientTransport());

        return collection;
    }
}
