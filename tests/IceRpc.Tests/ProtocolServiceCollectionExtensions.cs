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
            provider => new SlicServerTransport(provider.GetRequiredService<IDuplexServerTransport>()));
        services.AddSingleton<IMultiplexedClientTransport>(
            provider => new SlicClientTransport(provider.GetRequiredService<IDuplexClientTransport>()));

        services.AddSingleton<IDuplexListener, DuplexListenerDecorator>();
        services.AddSingleton<IMultiplexedListener, MultiplexedListenerDecorator>();

        services.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.StreamErrorCodeConverter = IceRpcProtocol.Instance.MultiplexedStreamErrorCodeConverter);

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

/// <summary>A helper class to connect and provide access to a client and server protocol connection. It also ensures
/// the connections are correctly disposed.</summary>
internal abstract class ClientServerProtocolConnection : IClientServerProtocolConnection, IDisposable
{
    public IProtocolConnection Client { get; }

    public IProtocolConnection Server
    {
        get => _server ?? throw new InvalidOperationException("server connection not initialized");
        private protected set => _server = value;
    }

    private readonly Func<Task<IProtocolConnection>> _acceptServerConnectionAsync;
    private readonly ILogger _logger;
    private IProtocolConnection? _server;

    public async Task ConnectAsync()
    {
        Task clientProtocolConnectionTask = Client.ConnectAsync(CancellationToken.None);
        _server = await _acceptServerConnectionAsync();
        if (_logger != NullLogger.Instance)
        {
            _server = new LogProtocolConnectionDecorator(_server, _logger);
        }
        await _server.ConnectAsync(CancellationToken.None);
        await clientProtocolConnectionTask;
    }

    public void Dispose()
    {
        _ = Client.DisposeAsync().AsTask();
        _ = _server?.DisposeAsync().AsTask();
    }

    private protected ClientServerProtocolConnection(
        IProtocolConnection clientProtocolConnection,
        Func<Task<IProtocolConnection>> acceptServerConnectionAsync,
        ILogger logger)
    {
        _acceptServerConnectionAsync = acceptServerConnectionAsync;
        _logger = logger;
        if (logger != NullLogger.Instance)
        {
            Client = new LogProtocolConnectionDecorator(clientProtocolConnection, logger);
        }
        else
        {
            Client = clientProtocolConnection;
        }
    }
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI instantiated")]
internal sealed class ClientServerIceProtocolConnection : ClientServerProtocolConnection
{
    // This constructor must be public to be usable by DI container
#pragma warning disable CA2000 // the connection is disposed by the base class Dispose method
    public ClientServerIceProtocolConnection(
        IDuplexClientTransport clientTransport,
        IDuplexListener listener,
        ILogger logger,
        IOptions<ClientConnectionOptions> clientConnectionOptions,
        IOptions<ServerOptions> serverOptions,
        IOptions<DuplexConnectionOptions> duplexConnectionOptions)
        : base(
            clientProtocolConnection: new IceProtocolConnection(
                    clientTransport.CreateConnection(
                        listener.Endpoint,
                        duplexConnectionOptions.Value,
                        clientConnectionOptions.Value.ClientAuthenticationOptions),
                isServer: false,
                clientConnectionOptions.Value),
            acceptServerConnectionAsync: async () => new IceProtocolConnection(
                    await listener.AcceptAsync(),
                    isServer: true,
                    serverOptions.Value.ConnectionOptions),
            logger)
    {
    }
#pragma warning restore CA2000
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI instantiated")]
internal sealed class ClientServerIceRpcProtocolConnection : ClientServerProtocolConnection
{
    // This constructor must be public to be usable by DI container
#pragma warning disable CA2000 // the connection is disposed by the base class Dispose method
    public ClientServerIceRpcProtocolConnection(
        IMultiplexedClientTransport clientTransport,
        IMultiplexedListener listener,
        ILogger logger,
        IOptions<ClientConnectionOptions> clientConnectionOptions,
        IOptions<ServerOptions> serverOptions,
        IOptions<MultiplexedConnectionOptions> multiplexedConnectionOptions)
        : base(
            clientProtocolConnection: new IceRpcProtocolConnection(
                    clientTransport.CreateConnection(
                        listener.Endpoint,
                        multiplexedConnectionOptions.Value,
                        clientConnectionOptions.Value.ClientAuthenticationOptions),
                clientConnectionOptions.Value),
            acceptServerConnectionAsync: async () => new IceRpcProtocolConnection(
                    await listener.AcceptAsync(),
                    serverOptions.Value.ConnectionOptions),
            logger)
    {
    }
#pragma warning restore CA2000
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI instantiated")]
internal class DuplexListenerDecorator : IDuplexListener
{
    private readonly IDuplexListener _listener;

    public Endpoint Endpoint => _listener.Endpoint;

    public DuplexListenerDecorator(
        IDuplexServerTransport serverTransport,
        ILogger logger,
        IOptions<ServerOptions> serverOptions,
        IOptions<DuplexConnectionOptions> duplexConnectionOptions)
    {
        _listener = serverTransport.Listen(
            serverOptions.Value.Endpoint,
            duplexConnectionOptions.Value,
            serverOptions.Value.ServerAuthenticationOptions);
        if (logger != NullLogger.Instance)
        {
            _listener = new LogDuplexListenerDecorator(_listener, logger);
        }
    }

    public Task<IDuplexConnection> AcceptAsync() => _listener.AcceptAsync();

    public void Dispose() => _listener.Dispose();
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI instantiated")]
internal class MultiplexedListenerDecorator : IMultiplexedListener
{
    private readonly IMultiplexedListener _listener;

    public Endpoint Endpoint => _listener.Endpoint;

    public MultiplexedListenerDecorator(
        IMultiplexedServerTransport serverTransport,
        ILogger logger,
        IOptions<ServerOptions> serverOptions,
        IOptions<MultiplexedConnectionOptions> multiplexedConnectionOptions)
    {
        _listener = serverTransport.Listen(
            serverOptions.Value.Endpoint,
            multiplexedConnectionOptions.Value,
            serverOptions.Value.ServerAuthenticationOptions);
        if (logger != NullLogger.Instance)
        {
            _listener = new LogMultiplexedListenerDecorator(_listener, logger);
        }
    }

    public Task<IMultiplexedConnection> AcceptAsync() => _listener.AcceptAsync();

    public void Dispose() => _listener.Dispose();
}
