// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Net;

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
                options.ServerAddress = new ServerAddress(protocol) { Host = "colochost" };
                options.ConnectionOptions.Dispatcher = dispatcher ?? ServiceNotFoundDispatcher.Instance;
            });

        services.AddSingleton<IMultiplexedServerTransport>(
            provider => new SlicServerTransport(provider.GetRequiredService<IDuplexServerTransport>()));
        services.AddSingleton<IMultiplexedClientTransport>(
            provider => new SlicClientTransport(provider.GetRequiredService<IDuplexClientTransport>()));

        services.AddSingleton<IListener<IDuplexConnection>, DuplexListenerDecorator>();
        services.AddSingleton<IListener<IMultiplexedConnection>, MultiplexedListenerDecorator>();

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
    private IProtocolConnection? _server;

    public async Task ConnectAsync()
    {
        Task clientProtocolConnectionTask = Client.ConnectAsync(CancellationToken.None);
        _server = await _acceptServerConnectionAsync();
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
        Func<Task<IProtocolConnection>> acceptServerConnectionAsync)
    {
        _acceptServerConnectionAsync = acceptServerConnectionAsync;
        Client = clientProtocolConnection;
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
        IListener<IDuplexConnection> listener,
        IOptions<ClientConnectionOptions> clientConnectionOptions,
        IOptions<ServerOptions> serverOptions,
        IOptions<DuplexConnectionOptions> duplexConnectionOptions)
        : base(
            clientProtocolConnection: new IceProtocolConnection(
                clientTransport.CreateConnection(
                    listener.ServerAddress,
                    duplexConnectionOptions.Value,
                    clientConnectionOptions.Value.ClientAuthenticationOptions),
                isServer: false,
                clientConnectionOptions.Value),
            acceptServerConnectionAsync: async () => new IceProtocolConnection(
                (await listener.AcceptAsync()).Connection,
                isServer: true,
                serverOptions.Value.ConnectionOptions))
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
        IListener<IMultiplexedConnection> listener,
        IOptions<ClientConnectionOptions> clientConnectionOptions,
        IOptions<ServerOptions> serverOptions,
        IOptions<MultiplexedConnectionOptions> multiplexedConnectionOptions)
        : base(
            clientProtocolConnection: new IceRpcProtocolConnection(
                clientTransport.CreateConnection(
                    listener.ServerAddress,
                    multiplexedConnectionOptions.Value,
                    clientConnectionOptions.Value.ClientAuthenticationOptions),
                isServer: false,
                clientConnectionOptions.Value),
            acceptServerConnectionAsync: async () => new IceRpcProtocolConnection(
                (await listener.AcceptAsync()).Connection,
                isServer: true,
                serverOptions.Value.ConnectionOptions))
    {
    }
#pragma warning restore CA2000
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI instantiated")]
internal class DuplexListenerDecorator : IListener<IDuplexConnection>
{
    public ServerAddress ServerAddress => _listener.ServerAddress;

    private readonly IListener<IDuplexConnection> _listener;

    public DuplexListenerDecorator(
        IDuplexServerTransport serverTransport,
        IOptions<ServerOptions> serverOptions,
        IOptions<DuplexConnectionOptions> duplexConnectionOptions) =>
        _listener = serverTransport.Listen(
            serverOptions.Value.ServerAddress,
            duplexConnectionOptions.Value,
            serverOptions.Value.ServerAuthenticationOptions);

    public Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync() => _listener.AcceptAsync();

    public void Dispose() => _listener.Dispose();
}

[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "DI instantiated")]
internal class MultiplexedListenerDecorator : IListener<IMultiplexedConnection>
{
    public ServerAddress ServerAddress => _listener.ServerAddress;

    private readonly IListener<IMultiplexedConnection> _listener;

    public MultiplexedListenerDecorator(
        IMultiplexedServerTransport serverTransport,
        IOptions<ServerOptions> serverOptions,
        IOptions<MultiplexedConnectionOptions> multiplexedConnectionOptions) =>
        _listener = serverTransport.Listen(
            serverOptions.Value.ServerAddress,
            multiplexedConnectionOptions.Value,
            serverOptions.Value.ServerAuthenticationOptions);

    public Task<(IMultiplexedConnection Connection, EndPoint RemoteNetworkAddress)> AcceptAsync() =>
        _listener.AcceptAsync();

    public void Dispose() => _listener.Dispose();
}
