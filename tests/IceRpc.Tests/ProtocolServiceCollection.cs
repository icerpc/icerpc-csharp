// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using System.Net.Security;

namespace IceRpc.Tests;

internal interface IClientServerProtocolConnection
{
    IProtocolConnection Client { get; }
    IProtocolConnection Server { get; }
    Task ConnectAsync(bool accept = true);
}

/// <summary>A helper struct to ensure the network and protocol connections are correctly disposed.</summary>
internal class ClientServerProtocolConnection<T> : IClientServerProtocolConnection, IDisposable
    where T : INetworkConnection
{
    public IProtocolConnection Client => _client ?? throw new InvalidOperationException("client connection not initialized");
    public IProtocolConnection Server => _server ?? throw new InvalidOperationException("server connection not initialized");

    private IProtocolConnection? _client;
    private readonly ConnectionOptions _clientConnectionOptions;
    private readonly IClientTransport<T> _clientTransport;
    private readonly IListener<T> _listener;
    private readonly Protocol _protocol;
    private readonly IProtocolConnectionFactory<T> _protocolConnectionFactory;
    private IProtocolConnection? _server;
    private readonly ConnectionOptions _serverConnectionOptions;

    public async Task ConnectAsync(bool accept = true)
    {
        T clientNetworkConnection = _clientTransport.CreateConnection(_listener.Endpoint, null, NullLogger.Instance);
        var connectTask = clientNetworkConnection.ConnectAsync(CancellationToken.None);
        T serverNetworkConnection = await _listener.AcceptAsync();
        await serverNetworkConnection.ConnectAsync(CancellationToken.None);
        await connectTask;

        var serverTask = _protocolConnectionFactory.CreateProtocolConnectionAsync(
            serverNetworkConnection,
            networkConnectionInformation: new(),
            true,
            _serverConnectionOptions,
            CancellationToken.None);

        IProtocolConnection clientProtocolConnection = await _protocolConnectionFactory.CreateProtocolConnectionAsync(
            clientNetworkConnection,
            networkConnectionInformation: new(),
            false,
            _clientConnectionOptions,
            CancellationToken.None);

        IProtocolConnection serverProtocolConnection = await serverTask;

        _client = clientProtocolConnection;
        _server = serverProtocolConnection;

        if (accept)
        {
            IConnection connection = _protocol == Protocol.Ice ? InvalidConnection.Ice : InvalidConnection.IceRpc;
            _ = _client.AcceptRequestsAsync(connection);
            _ = _server.AcceptRequestsAsync(connection);
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
        _server?.Dispose();
    }

    internal ClientServerProtocolConnection(
        Protocol protocol,
        IProtocolConnectionFactory<T> protocolConnectionFactory,
        IClientTransport<T> clientTransport,
        IListener<T> listener,
        ConnectionOptions clientConnectionOptions,
        ConnectionOptions serverConnectionOptions)
    {
        _protocol = protocol;
        _protocolConnectionFactory = protocolConnectionFactory;
        _clientTransport = clientTransport;
        _listener = listener;
        _clientConnectionOptions = clientConnectionOptions;
        _serverConnectionOptions = serverConnectionOptions;
        _client = null;
        _server = null;
    }
}

public static class ProtocolServiceCollectionExtensions
{
    public static IServiceCollection AddProtocolTest(
        this IServiceCollection services,
        Protocol protocol,
        IDispatcher? dispatcher = null)
    {
        var endpoint = new Endpoint(protocol) { Host = "colochost" };
        services.AddColocTransport();

        if (dispatcher != null)
        {
            services.AddOptions<ServerOptions>().Configure(
                options =>
                {
                    options.ConnectionOptions.Dispatcher = dispatcher;
                });
        }

        services.TryAddSingleton<IConnection>(
            protocol == Protocol.Ice ? InvalidConnection.Ice : InvalidConnection.IceRpc);

        services.AddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(
            provider => new SlicServerTransport(
                provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));
        services.AddSingleton<IClientTransport<IMultiplexedNetworkConnection>>(
            provider => new SlicClientTransport(
                provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

        services.AddSingleton(IceProtocol.Instance.ProtocolConnectionFactory);
        services.AddSingleton(IceRpcProtocol.Instance.ProtocolConnectionFactory);
        services.AddSingleton(provider => CreateListener<ISimpleNetworkConnection>(provider, endpoint));
        services.AddSingleton(provider => CreateListener<IMultiplexedNetworkConnection>(provider, endpoint));

        if (protocol == Protocol.Ice)
        {
            services.AddSingleton<IClientServerProtocolConnection>(provider =>
                new ClientServerProtocolConnection<ISimpleNetworkConnection>(
                    protocol,
                    provider.GetRequiredService<IProtocolConnectionFactory<ISimpleNetworkConnection>>(),
                    provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>(),
                    provider.GetRequiredService<IListener<ISimpleNetworkConnection>>(),
                    provider.GetService<IOptions<ConnectionOptions>>()?.Value ?? new ConnectionOptions(),
                    provider.GetService<IOptions<ServerOptions>>()?.Value?.ConnectionOptions ?? new ConnectionOptions()));
        }
        else
        {
            services.AddSingleton<IClientServerProtocolConnection>(provider =>
                new ClientServerProtocolConnection<IMultiplexedNetworkConnection>(
                    protocol,
                    provider.GetRequiredService<IProtocolConnectionFactory<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>(),
                    provider.GetRequiredService<IListener<IMultiplexedNetworkConnection>>(),
                    provider.GetService<IOptions<ConnectionOptions>>()?.Value ?? new ConnectionOptions(),
                    provider.GetService<IOptions<ServerOptions>>()?.Value?.ConnectionOptions ?? new ConnectionOptions()));
        }
        return services;

        static IListener<T> CreateListener<T>(IServiceProvider serviceProvider, Endpoint endpoint)
            where T : INetworkConnection
        {
            ILogger logger = serviceProvider.GetService<ILogger>() ?? NullLogger.Instance;

            IListener<T> listener =
                serviceProvider.GetRequiredService<IServerTransport<T>>().Listen(
                    endpoint,
                    serviceProvider.GetService<SslServerAuthenticationOptions>(),
                    logger);

            if (logger != NullLogger.Instance)
            {
                LogNetworkConnectionDecoratorFactory<T>? decorator =
                    serviceProvider.GetService<LogNetworkConnectionDecoratorFactory<T>>();
                if (decorator != null)
                {
                    listener = new LogListenerDecorator<T>(listener, logger, decorator);
                }
            }
            return listener;
        }
    }
}
