// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Transports;
using NUnit.Framework;
using System.Net.Security;

namespace IceRpc.Tests;

public sealed class ConnectionCacheTests
{
    /// <summary>Verifies that the connection cache does not prefer existing connections when
    /// <c>preferExistingConnection</c> is <see langword="false" />.</summary>
    [Test]
    public async Task Do_not_prefer_existing_connection()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request)));
        var colocTransport = new ColocTransport();
        await using var server1 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://foo"))
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        ServerAddress server1Address = server1.Listen();

        await using var server2 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://bar")),
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        ServerAddress server2Address = server2.Listen();

        await using var cache = new ConnectionCache(
            new ConnectionCacheOptions { PreferExistingConnection = false },
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        ServerAddress? serverAddress = null;
        Pipeline pipeline = new Pipeline()
            .Use(next => new InlineInvoker(async (request, cancellationToken) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancellationToken);
                    serverAddress = request.Features.Get<IServerAddressFeature>()?.ServerAddress;
                    return response;
                }))
            .Into(cache);

        await new ServiceProxy(cache, new Uri("icerpc://bar")).IcePingAsync();

        // Act
        await new ServiceProxy(pipeline, new Uri("icerpc://foo/?alt-server=bar")).IcePingAsync();

        // Assert
        Assert.That(serverAddress?.Host, Is.EqualTo(server1Address.Host));
        Assert.That(server1Address, Is.Not.EqualTo(server2Address));
    }

    /// <summary>Verifies that the connection cache uses the alt-server when it cannot connect to the main server address.
    /// </summary>
    [Test]
    public async Task Get_connection_for_alt_server()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request)));
        var colocTransport = new ColocTransport();
        await using var server = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://foo"))
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        ServerAddress serverAddress = server.Listen();

        await using var cache = new ConnectionCache(
            new ConnectionCacheOptions(),
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        ServerAddress? selectedServerAddress = null;
        Pipeline pipeline = new Pipeline()
            .Use(next => new InlineInvoker(async (request, cancellationToken) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancellationToken);
                    selectedServerAddress = request.Features.Get<IServerAddressFeature>()?.ServerAddress;
                    return response;
                }))
            .Into(cache);

        // Act
        await new ServiceProxy(pipeline, new Uri("icerpc://bar/?alt-server=foo")).IcePingAsync();

        // Assert
        Assert.That(selectedServerAddress?.Host, Is.EqualTo(serverAddress.Host));
    }

    /// <summary>Verifies that the connection cache prefers connecting to the main server address.</summary>
    [Test]
    public async Task Get_connection_for_main_server_address()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request)));
        var colocTransport = new ColocTransport();
        await using var server1 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://foo"))
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        ServerAddress server1Address = server1.Listen();

        await using var server2 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://bar"))
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        ServerAddress server2Address = server2.Listen();

        await using var cache = new ConnectionCache(
            new ConnectionCacheOptions(),
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        ServerAddress? serverAddress = null;
        Pipeline pipeline = new Pipeline()
            .Use(next => new InlineInvoker(async (request, cancellationToken) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancellationToken);
                    serverAddress = request.Features.Get<IServerAddressFeature>()?.ServerAddress;
                    return response;
                }))
            .Into(cache);

        // Act
        await new ServiceProxy(pipeline, new Uri("icerpc://foo/?alt-server=bar")).IcePingAsync();

        // Assert
        Assert.That(serverAddress?.Host, Is.EqualTo(server1Address.Host));
    }

    /// <summary>Verifies that the connection cache prefers reusing an existing connection when
    /// <c>preferExistingConnection</c> is <see langword="true" />.</summary>
    [Test]
    public async Task Prefer_existing_connection()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request)));
        var colocTransport = new ColocTransport();
        await using var server1 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://foo"))
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        ServerAddress server1Address = server1.Listen();

        await using var server2 = new Server(
            new ServerOptions()
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://bar"))
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        ServerAddress server2Address = server2.Listen();

        await using var cache = new ConnectionCache(
           new ConnectionCacheOptions(),
           multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        ServerAddress? serverAddress = null;
        Pipeline pipeline = new Pipeline()
            .Use(next => new InlineInvoker(async (request, cancellationToken) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancellationToken);
                    serverAddress = request.Features.Get<IServerAddressFeature>()?.ServerAddress;
                    return response;
                }))
            .Into(cache);

        await new ServiceProxy(cache, new Uri("icerpc://bar")).IcePingAsync();

        // Act
        await new ServiceProxy(pipeline, new Uri("icerpc://foo/?alt-server=bar")).IcePingAsync();

        // Assert
        Assert.That(serverAddress?.Host, Is.EqualTo(server2Address.Host));
        Assert.That(server1Address, Is.Not.EqualTo(server2Address));
    }

    [Test]
    public async Task Dispose_waits_for_background_connection_dispose()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancellationToken) => new(new OutgoingResponse(request)));

        var colocTransport = new ColocTransport();
        var clientTransport = new SlowDisposeClientTransport(new SlicClientTransport(colocTransport.ClientTransport));
        await using var server = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://foo"))
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server.Listen();

        await using var cache = new ConnectionCache(
           new ConnectionCacheOptions(),
           multiplexedClientTransport: clientTransport);

        // Make a request to establish a connection
        await new ServiceProxy(cache, new Uri("icerpc://foo")).IcePingAsync();

        // Get the last connection created by the cache
        SlowDisposeConnection connection = clientTransport.LastConnection!;

        // Shutdown the server. This will trigger connection closure.
        await server.ShutdownAsync();

        await connection.WaitForDisposeAsync(CancellationToken.None);

        // Act
        ValueTask disposeTask = cache.DisposeAsync();

        // Assert
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        Assert.That(disposeTask.IsCompleted, Is.False);
        connection.ReleaseDispose();
        await disposeTask;
    }

    private sealed class SlowDisposeClientTransport : IMultiplexedClientTransport
    {
        public string Name => _transport.Name;

        public SlowDisposeConnection? LastConnection { get; set; }

        private readonly IMultiplexedClientTransport _transport;

        public bool CheckParams(ServerAddress serverAddress) => _transport.CheckParams(serverAddress);
        public SlowDisposeClientTransport(IMultiplexedClientTransport transport) => _transport = transport;

        public IMultiplexedConnection CreateConnection(
            ServerAddress serverAddress,
            MultiplexedConnectionOptions options,
            SslClientAuthenticationOptions? clientAuthenticationOptions)
        {
            IMultiplexedConnection connection = _transport.CreateConnection(
                serverAddress,
                options,
                clientAuthenticationOptions);
            Assert.That(LastConnection, Is.Null);
            LastConnection = new SlowDisposeConnection(connection);
            return LastConnection;
        }
    }

    private sealed class SlowDisposeConnection : IMultiplexedConnection
    {
        public ServerAddress ServerAddress => _connection.ServerAddress;

        private readonly IMultiplexedConnection _connection;

        private readonly TaskCompletionSource _continueDisposeTcs = new();
        private readonly SemaphoreSlim _waitDisposeSemaphore = new(0);

        public SlowDisposeConnection(IMultiplexedConnection connection) => _connection = connection;

        public ValueTask<IMultiplexedStream> AcceptStreamAsync(CancellationToken cancellationToken) =>
            _connection.AcceptStreamAsync(cancellationToken);

        public Task<TransportConnectionInformation> ConnectAsync(CancellationToken cancellationToken) =>
            _connection.ConnectAsync(cancellationToken);

        public Task CloseAsync(MultiplexedConnectionCloseError closeError, CancellationToken cancellationToken) =>
            _connection.CloseAsync(closeError, cancellationToken);

        public ValueTask<IMultiplexedStream> CreateStreamAsync(bool bidirectional, CancellationToken cancellationToken) =>
            _connection.CreateStreamAsync(bidirectional, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            _waitDisposeSemaphore.Release();
            await _continueDisposeTcs.Task.ConfigureAwait(false);
            await _connection.DisposeAsync().ConfigureAwait(false);
            _waitDisposeSemaphore.Dispose();
        }

        public Task WaitForDisposeAsync(CancellationToken cancellationToken) =>
            _waitDisposeSemaphore.WaitAsync(cancellationToken);

        public void ReleaseDispose() => _continueDisposeTcs.SetResult();
    }
}
