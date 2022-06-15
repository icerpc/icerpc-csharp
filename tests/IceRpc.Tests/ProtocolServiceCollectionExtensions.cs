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

namespace IceRpc.Tests;

public static class ProtocolServiceCollectionExtensions
{
    public static IServiceCollection AddProtocolTest(
        this IServiceCollection services,
        Protocol protocol,
        IDispatcher? dispatcher = null)
    {
        services.AddColocTransport();

        services.AddOptions<ServerOptions>().Configure(
            options =>
            {
                options.Endpoint = new Endpoint(protocol) { Host = "colochost" };
                if (dispatcher != null)
                {
                    options.ConnectionOptions.Dispatcher = dispatcher;
                }
            });

        services.TryAddSingleton<ILogger>(NullLogger.Instance);

        services.AddSingleton<IServerTransport<IMultiplexedNetworkConnection>>(
            provider => new SlicServerTransport(
                provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>()));

        services.AddSingleton<IClientTransport<IMultiplexedNetworkConnection>>(
            provider => new SlicClientTransport(
                provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>()));

        services.AddSingleton<IListener<ISimpleNetworkConnection>, Listener<ISimpleNetworkConnection>>();

        services.AddSingleton<IListener<IMultiplexedNetworkConnection>, Listener<IMultiplexedNetworkConnection>>();

        services.AddSingleton<LogNetworkConnectionDecoratorFactory<ISimpleNetworkConnection>>(
            provider => (ISimpleNetworkConnection decoratee, Endpoint endpoint, bool isServer, ILogger logger) =>
                new LogSimpleNetworkConnectionDecorator(decoratee, endpoint, isServer, logger));

        services.AddSingleton<LogNetworkConnectionDecoratorFactory<IMultiplexedNetworkConnection>>(
            provider => (IMultiplexedNetworkConnection decoratee, Endpoint endpoint, bool isServer, ILogger logger) =>
                new LogMultiplexedNetworkConnectionDecorator(decoratee, endpoint, isServer, logger));

        if (protocol == Protocol.Ice)
        {
            services.TryAddSingleton<IConnection>(InvalidConnection.Ice);
            services.AddSingleton(IceProtocol.Instance.ProtocolConnectionFactory);
            services.AddSingleton<IClientServerProtocolConnection, ClientServerProtocolConnection<ISimpleNetworkConnection>>();
        }
        else
        {
            services.TryAddSingleton<IConnection>(InvalidConnection.IceRpc);
            services.AddSingleton(IceRpcProtocol.Instance.ProtocolConnectionFactory);
            services.AddSingleton<IClientServerProtocolConnection, ClientServerProtocolConnection<IMultiplexedNetworkConnection>>();
        }
        return services;
    }
}

internal interface IClientServerProtocolConnection
{
    IProtocolConnection Client { get; }
    IProtocolConnection Server { get; }

    Task ConnectAsync(
        Action? onClientIdle = null,
        Action<string>? onClientShutdown = null,
        Action? onServerIdle = null,
        Action<string>? onServerShutdown = null,
        bool acceptRequests = true);
}

/// <summary>A helper class to connect and provide access to a client and server protocol connection. It also  ensures
/// the connections are correctly disposed.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812: Avoid uninstantiated internal classes",
    Justification = "DI instantiated")]
internal class ClientServerProtocolConnection<T> : IClientServerProtocolConnection, IDisposable
    where T : INetworkConnection
{
    public IProtocolConnection Client =>
        _client ?? throw new InvalidOperationException("client connection not initialized");
    public IProtocolConnection Server =>
        _server ?? throw new InvalidOperationException("server connection not initialized");

    private IProtocolConnection? _client;
    private readonly ConnectionOptions _clientConnectionOptions;
    private readonly IClientTransport<T> _clientTransport;
    private readonly IConnection _connection;
    private readonly IListener<T> _listener;
    private readonly IProtocolConnectionFactory<T> _protocolConnectionFactory;
    private IProtocolConnection? _server;
    private readonly ServerOptions _serverOptions;

    public async Task ConnectAsync(
        Action? onClientIdle,
        Action<string>? onClientShutdown,
        Action? onServerIdle,
        Action<string>? onServerShutdown,
        bool acceptRequests = true)
    {
        Task<IProtocolConnection> clientProtocolConnectionTask = CreateConnectionAsync(
            _clientTransport.CreateConnection(_listener.Endpoint, null, NullLogger.Instance),
            _clientConnectionOptions,
            isServer: false,
            onClientIdle,
            onClientShutdown);

        Task<IProtocolConnection> serverProtocolConnectionTask = CreateConnectionAsync(
            await _listener.AcceptAsync(),
            _serverOptions.ConnectionOptions,
            isServer: true,
            onServerIdle,
            onServerShutdown);

        _client = await clientProtocolConnectionTask;
        _server = await serverProtocolConnectionTask;

        if (acceptRequests)
        {
            _ = _client.AcceptRequestsAsync(_connection);
            _ = _server.AcceptRequestsAsync(_connection);
        }

        async Task<IProtocolConnection> CreateConnectionAsync(
            T networkConnection,
            ConnectionOptions connectionOptions,
            bool isServer,
            Action? onIdle,
            Action<string>? onShutdown)
        {
            IProtocolConnection protocolConnection = _protocolConnectionFactory.CreateConnection(
                networkConnection,
                connectionOptions);
            _ = await protocolConnection.ConnectAsync(
                isServer,
                onIdle ?? (() => {}),
                onShutdown ?? (_ => {}),
                CancellationToken.None);
            return protocolConnection;
        }
    }

    public void Dispose()
    {
        _client?.Abort(new ConnectionClosedException());
        _server?.Abort(new ConnectionClosedException());
    }

    // This constructor must be public to be usable by DI container
    public ClientServerProtocolConnection(
        IConnection connection,
        IProtocolConnectionFactory<T> protocolConnectionFactory,
        IClientTransport<T> clientTransport,
        IListener<T> listener,
        IOptions<ConnectionOptions> clientConnectionOptions,
        IOptions<ServerOptions> serverOptions)
    {
        _connection = connection;
        _protocolConnectionFactory = protocolConnectionFactory;
        _clientTransport = clientTransport;
        _listener = listener;
        _clientConnectionOptions = clientConnectionOptions?.Value ?? new ConnectionOptions();
        _serverOptions = serverOptions?.Value ?? new ServerOptions();
        _client = null;
        _server = null;
    }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI instantiated")]
internal class Listener<T> : IListener<T> where T : INetworkConnection
{
    private readonly IListener<T> _listener;

    public Endpoint Endpoint => _listener.Endpoint;

    public Listener(
        IServerTransport<T> serverTransport,
        ILogger logger,
        IOptions<ServerOptions> serverOptions,
        LogNetworkConnectionDecoratorFactory<T> logDecoratorFactory)
    {
        _listener = serverTransport.Listen(
            serverOptions.Value.Endpoint,
            serverOptions.Value.ServerAuthenticationOptions,
            logger);
        if (logger != NullLogger.Instance)
        {
            _listener = new LogListenerDecorator<T>(_listener, logger, logDecoratorFactory);
        }
    }

    public Task<T> AcceptAsync() => _listener.AcceptAsync();
    public ValueTask DisposeAsync() => _listener.DisposeAsync();
}
