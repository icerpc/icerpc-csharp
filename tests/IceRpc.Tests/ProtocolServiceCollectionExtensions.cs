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
            services.TryAddSingleton(InvalidConnection.Ice);
            services.AddSingleton<IClientServerProtocolConnection, ClientServerIceProtocolConnection>();
        }
        else
        {
            services.TryAddSingleton(InvalidConnection.IceRpc);
            services.AddSingleton<IClientServerProtocolConnection, ClientServerIceRpcProtocolConnection>();
        }
        return services;
    }
}

internal interface IClientServerProtocolConnection
{
    IProtocolConnection Client { get; }
    IProtocolConnection Server { get; }

    Task ConnectAsync();
}

/// <summary>A helper class to connect and provide access to a client and server protocol connection. It also  ensures
/// the connections are correctly disposed.</summary>
internal abstract class ClientServerProtocolConnection<T> : IClientServerProtocolConnection, IDisposable
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
    private IProtocolConnection? _server;
    private readonly ServerOptions _serverOptions;

    public async Task ConnectAsync()
    {
        Task<IProtocolConnection> clientProtocolConnectionTask = CreateConnectionAsync(
            _clientTransport.CreateConnection(_listener.Endpoint, null, NullLogger.Instance),
            _clientConnectionOptions,
            isServer: false);

        Task<IProtocolConnection> serverProtocolConnectionTask = CreateConnectionAsync(
            await _listener.AcceptAsync(),
            _serverOptions.ConnectionOptions,
            isServer: true);

        _client = await clientProtocolConnectionTask;
        _server = await serverProtocolConnectionTask;

        async Task<IProtocolConnection> CreateConnectionAsync(
            T networkConnection,
            ConnectionOptions connectionOptions,
            bool isServer)
        {
            IProtocolConnection protocolConnection = CreateConnection(networkConnection, connectionOptions);
            _ = await protocolConnection.ConnectAsync(
                isServer,
                _connection,
                CancellationToken.None);
            return protocolConnection;
        }
    }

    public void Dispose()
    {
        _client?.Abort(new ConnectionClosedException());
        _server?.Abort(new ConnectionClosedException());
    }

    protected ClientServerProtocolConnection(
        IConnection connection,
        IClientTransport<T> clientTransport,
        IListener<T> listener,
        IOptions<ConnectionOptions> clientConnectionOptions,
        IOptions<ServerOptions> serverOptions)
    {
        _connection = connection;
        _clientTransport = clientTransport;
        _listener = listener;
        _clientConnectionOptions = clientConnectionOptions?.Value ?? new ConnectionOptions();
        _serverOptions = serverOptions?.Value ?? new ServerOptions();
        _client = null;
        _server = null;
    }

    protected abstract IProtocolConnection CreateConnection(T networkConnection, ConnectionOptions options);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI instantiated")]
internal sealed class ClientServerIceProtocolConnection : ClientServerProtocolConnection<ISimpleNetworkConnection>
{
    // This constructor must be public to be usable by DI container
    public ClientServerIceProtocolConnection(
        IConnection connection,
        IClientTransport<ISimpleNetworkConnection> clientTransport,
        IListener<ISimpleNetworkConnection> listener,
        IOptions<ConnectionOptions> clientConnectionOptions,
        IOptions<ServerOptions> serverOptions) :
        base(connection, clientTransport, listener, clientConnectionOptions, serverOptions)
    {
    }

    protected override IProtocolConnection CreateConnection(
        ISimpleNetworkConnection networkConnection,
        ConnectionOptions options) =>
        new IceProtocolConnection(networkConnection, options);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI instantiated")]
internal sealed class ClientServerIceRpcProtocolConnection :
    ClientServerProtocolConnection<IMultiplexedNetworkConnection>
{
    // This constructor must be public to be usable by DI container
    public ClientServerIceRpcProtocolConnection(
        IConnection connection,
        IClientTransport<IMultiplexedNetworkConnection> clientTransport,
        IListener<IMultiplexedNetworkConnection> listener,
        IOptions<ConnectionOptions> clientConnectionOptions,
        IOptions<ServerOptions> serverOptions) :
        base(connection, clientTransport, listener, clientConnectionOptions, serverOptions)
    {
    }

    protected override IProtocolConnection CreateConnection(
        IMultiplexedNetworkConnection networkConnection,
        ConnectionOptions options) =>
        new IceRpcProtocolConnection(networkConnection, options);
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

    public void Dispose() => _listener.Dispose();
}
