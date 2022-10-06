// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;

namespace IceRpc.Tests;

public static class ProtocolServiceCollectionExtensions
{
    public static IServiceCollection AddProtocolTest(
        this IServiceCollection services,
        Protocol protocol,
        IDispatcher? dispatcher = null,
        ConnectionOptions? clientConnectionOptions = null,
        ConnectionOptions? serverConnectionOptions = null)
    {
        clientConnectionOptions ??= new();
        serverConnectionOptions ??= new();
        serverConnectionOptions.Dispatcher ??= dispatcher;

        if (protocol == Protocol.Ice)
        {
            services.AddIceProtocolTest(clientConnectionOptions, serverConnectionOptions);
        }
        else
        {
            services.AddIceRpcProtocolTest(clientConnectionOptions, serverConnectionOptions);
        }
        return services;
    }

    private static IServiceCollection AddIceProtocolTest(
        this IServiceCollection services,
        ConnectionOptions clientConnectionOptions,
        ConnectionOptions serverConnectionOptions)
    {
        services
            .AddColocTransport()
            .AddDuplexTransportClientServerTest(new Uri("ice://colochost"));

        services.AddSingleton(provider =>
            new ClientServerProtocolConnection(
                clientProtocolConnection: new IceProtocolConnection(
                    provider.GetRequiredService<IDuplexConnection>(),
                    isServer: false,
                    clientConnectionOptions),
                acceptServerConnectionAsync: async () => new IceProtocolConnection(
                    (await provider.GetRequiredService<IListener<IDuplexConnection>>().AcceptAsync(default)).Connection,
                    isServer: true,
                    serverConnectionOptions),
                listener: provider.GetRequiredService<IListener<IDuplexConnection>>()));

        return services;
    }

    private static IServiceCollection AddIceRpcProtocolTest(
        this IServiceCollection services,
        ConnectionOptions clientConnectionOptions,
        ConnectionOptions serverConnectionOptions)
    {
        services
            .AddColocTransport()
            .AddSlicTransport()
            .AddMultiplexedTransportClientServerTest(new Uri("icerpc://colochost"));

        services.AddSingleton(provider =>
            new ClientServerProtocolConnection(
                clientProtocolConnection: new IceRpcProtocolConnection(
                    provider.GetRequiredService<IMultiplexedConnection>(),
                    isServer: false,
                    clientConnectionOptions),
                acceptServerConnectionAsync: async () => new IceRpcProtocolConnection(
                    (await provider.GetRequiredService<IListener<IMultiplexedConnection>>().AcceptAsync(
                        default)).Connection,
                    isServer: true,
                    serverConnectionOptions),
                listener: provider.GetRequiredService<IListener<IMultiplexedConnection>>()));

        services.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.StreamErrorCodeConverter = IceRpcProtocol.Instance.MultiplexedStreamErrorCodeConverter);

        return services;
    }
}

/// <summary>A helper class to connect and provide access to a client and server protocol connection. It also ensures
/// the connections are correctly disposed.</summary>
internal class ClientServerProtocolConnection : IDisposable
{
    public IProtocolConnection Client { get; }

    public IProtocolConnection Server
    {
        get => _server ?? throw new InvalidOperationException("server connection not initialized");
        private protected set => _server = value;
    }

    private readonly Func<Task<IProtocolConnection>> _acceptServerConnectionAsync;
    private readonly IAsyncDisposable _listener;
    private IProtocolConnection? _server;

    public async Task ConnectAsync()
    {
        Task clientProtocolConnectionTask = Client.ConnectAsync(CancellationToken.None);
        await AcceptAsync();
        await clientProtocolConnectionTask;
    }

    public async Task AcceptAsync()
    {
        _server = await _acceptServerConnectionAsync();
        await _server.ConnectAsync(CancellationToken.None);
    }

    public void Dispose()
    {
        _ = Client.DisposeAsync().AsTask();
        _ = _server?.DisposeAsync().AsTask();
    }

    public ValueTask DisposeListenerAsync() => _listener.DisposeAsync();

    internal ClientServerProtocolConnection(
        IProtocolConnection clientProtocolConnection,
        Func<Task<IProtocolConnection>> acceptServerConnectionAsync,
        IAsyncDisposable listener)
    {
        _acceptServerConnectionAsync = acceptServerConnectionAsync;
        _listener = listener;
        Client = clientProtocolConnection;
    }
}
