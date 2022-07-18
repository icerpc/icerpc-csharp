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
                options.ConnectionOptions.Dispatcher = dispatcher ?? ServiceNotFoundDispatcher.Instance;
            });

        services.TryAddSingleton<ILogger>(NullLogger.Instance);

        services.AddSingleton<IMultiplexedServerTransport>(
            provider => new SlicServerTransport(
                provider.GetRequiredService<IDuplexServerTransport>()));

        services.AddSingleton<IMultiplexedClientTransport>(
            provider => new SlicClientTransport(
                provider.GetRequiredService<IDuplexClientTransport>()));

        services.AddSingleton<IDuplexListener,
                                        Listener<IDuplexConnection>>();

        services.AddSingleton<IMultiplexedListener, Listener<IMultiplexedConnection>>();

        services.AddSingleton<LogTransportConnectionDecoratorFactory<IDuplexConnection>>(
            provider =>
                (IDuplexConnection decoratee, Endpoint endpoint, bool isServer, ILogger logger) =>
                    new LogDuplexConnectionDecorator(decoratee, endpoint, isServer, logger));

        services.AddSingleton<LogTransportConnectionDecoratorFactory<IMultiplexedConnection>>(
            provider =>
                (IMultiplexedConnection decoratee, Endpoint endpoint, bool isServer, ILogger logger) =>
                    new LogMultiplexedConnectionDecorator(decoratee, endpoint, isServer, logger));

        if (protocol == Protocol.Ice)
        {
            services.AddSingleton<IClientServerProtocolConnection, ClientServerIceProtocolConnection>();
        }
        else
        {
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
    where T : ITransportConnection
{
    public IProtocolConnection Client =>
        _client ?? throw new InvalidOperationException("client connection not initialized");
    public IProtocolConnection Server =>
        _server ?? throw new InvalidOperationException("server connection not initialized");

    private IProtocolConnection? _client;
    private readonly ConnectionOptions _clientConnectionOptions;
    private readonly IClientTransport<T> _clientTransport;
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
            T transportConnection,
            ConnectionOptions connectionOptions,
            bool isServer)
        {
            IProtocolConnection protocolConnection = CreateConnection(transportConnection, isServer, connectionOptions);
            _ = await protocolConnection.ConnectAsync(CancellationToken.None);
            return protocolConnection;
        }
    }

    public void Dispose()
    {
        ValueTask? disposeTask;
        disposeTask = _client?.DisposeAsync();
        disposeTask = _server?.DisposeAsync();
    }

    protected ClientServerProtocolConnection(
        IClientTransport<T> clientTransport,
        IListener<T> listener,
        IOptions<ConnectionOptions> clientConnectionOptions,
        IOptions<ServerOptions> serverOptions)
    {
        _clientTransport = clientTransport;
        _listener = listener;
        _clientConnectionOptions = clientConnectionOptions?.Value ?? new ConnectionOptions();
        _serverOptions = serverOptions?.Value ?? new ServerOptions();
        _client = null;
        _server = null;
    }

    protected abstract IProtocolConnection CreateConnection(
        T transportConnection,
        bool isServer,
        ConnectionOptions options);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI instantiated")]
internal sealed class ClientServerIceProtocolConnection :
    ClientServerProtocolConnection<IDuplexConnection>
{
    // This constructor must be public to be usable by DI container
    public ClientServerIceProtocolConnection(
        IDuplexClientTransport clientTransport,
        IDuplexListener listener,
        IOptions<ConnectionOptions> clientConnectionOptions,
        IOptions<ServerOptions> serverOptions) :
        base(clientTransport, listener, clientConnectionOptions, serverOptions)
    {
    }

    protected override IProtocolConnection CreateConnection(
        IDuplexConnection transportConnection,
        bool isServer,
        ConnectionOptions options) =>
        new IceProtocolConnection(transportConnection, isServer, options);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI instantiated")]
internal sealed class ClientServerIceRpcProtocolConnection :
    ClientServerProtocolConnection<IMultiplexedConnection>
{
    // This constructor must be public to be usable by DI container
    public ClientServerIceRpcProtocolConnection(
        IMultiplexedClientTransport clientTransport,
        IMultiplexedListener listener,
        IOptions<ConnectionOptions> clientConnectionOptions,
        IOptions<ServerOptions> serverOptions) :
        base(clientTransport, listener, clientConnectionOptions, serverOptions)
    {
    }

    protected override IProtocolConnection CreateConnection(
        IMultiplexedConnection transportConnection,
        bool isServer,
        ConnectionOptions options) =>
        new IceRpcProtocolConnection(transportConnection, options);
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI instantiated")]
internal class Listener<T> : IListener<T> where T : ITransportConnection
{
    private readonly IListener<T> _listener;

    public Endpoint Endpoint => _listener.Endpoint;

    public Listener(
        IServerTransport<T> serverTransport,
        ILogger logger,
        IOptions<ServerOptions> serverOptions,
        LogTransportConnectionDecoratorFactory<T> logDecoratorFactory)
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
