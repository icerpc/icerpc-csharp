// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using IceRpc.Tests.Slice;
using IceRpc.Transports.Coloc;
using IceRpc.Transports.Slic;
using NUnit.Framework;

namespace IceRpc.Tests;

public sealed class ConnectionCacheTests
{
    /// <summary>Verifies that the connection cache does not prefer existing connections when
    /// <c>preferExistingConnection</c> is <see langword="false" />.</summary>
    [Test]
    public async Task Do_not_prefer_existing_connection()
    {
        // Arrange
        using var dispatcher = new TestDispatcher();
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

        await new PingableProxy(cache, new Uri("icerpc://bar")).PingAsync();

        // Act
        await new PingableProxy(pipeline, new Uri("icerpc://foo/?alt-server=bar")).PingAsync();

        // Assert
        Assert.That(serverAddress?.Host, Is.EqualTo(server1Address.Host));
        Assert.That(server1Address, Is.Not.EqualTo(server2Address));

        // Cleanup
        await server1.ShutdownAsync();
        await server2.ShutdownAsync();
        await cache.ShutdownAsync();
    }

    /// <summary>Verifies that the connection cache uses the alt-server when it cannot connect to the main server address.
    /// </summary>
    [Test]
    public async Task Get_connection_for_alt_server()
    {
        // Arrange
        using var dispatcher = new TestDispatcher();
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
        await new PingableProxy(pipeline, new Uri("icerpc://bar/?alt-server=foo")).PingAsync();

        // Assert
        Assert.That(selectedServerAddress?.Host, Is.EqualTo(serverAddress.Host));

        // Cleanup
        await server.ShutdownAsync();
        await cache.ShutdownAsync();
    }

    /// <summary>Verifies that the connection cache prefers connecting to the main server address.</summary>
    [Test]
    public async Task Get_connection_for_main_server_address()
    {
        // Arrange
        using var dispatcher = new TestDispatcher();
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
        await new PingableProxy(pipeline, new Uri("icerpc://foo/?alt-server=bar")).PingAsync();

        // Assert
        Assert.That(serverAddress?.Host, Is.EqualTo(server1Address.Host));

        // Cleanup
        await server1.ShutdownAsync();
        await server2.ShutdownAsync();
        await cache.ShutdownAsync();
    }

    /// <summary>Verifies that the connection cache prefers reusing an existing connection when
    /// <c>preferExistingConnection</c> is <see langword="true" />.</summary>
    [Test]
    public async Task Prefer_existing_connection()
    {
        // Arrange
        using var dispatcher = new TestDispatcher();
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

        await new PingableProxy(cache, new Uri("icerpc://bar")).PingAsync();

        // Act
        await new PingableProxy(pipeline, new Uri("icerpc://foo/?alt-server=bar")).PingAsync();

        // Assert
        Assert.That(serverAddress?.Host, Is.EqualTo(server2Address.Host));
        Assert.That(server1Address, Is.Not.EqualTo(server2Address));

        // Cleanup
        await server1.ShutdownAsync();
        await server2.ShutdownAsync();
        await cache.ShutdownAsync();
    }

    [Test]
    public async Task Dispose_waits_for_background_connection_dispose()
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        var colocTransport = new ColocTransport();
        var multiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport);
        var multiplexedClientTransport = new TestMultiplexedClientTransportDecorator(
            new SlicClientTransport(colocTransport.ClientTransport));

        await using var server = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://foo"))
            },
            multiplexedServerTransport: multiplexedServerTransport);
        server.Listen();

        await using var cache = new ConnectionCache(
            options: new(),
            multiplexedClientTransport: multiplexedClientTransport);
        await new PingableProxy(cache, new Uri("icerpc://foo")).PingAsync();

        TestMultiplexedConnectionDecorator clientConnection = multiplexedClientTransport.LastCreatedConnection!;
        clientConnection.Operations.Hold = MultiplexedTransportOperations.Dispose;
        Task disposedCalledTask = clientConnection.Operations.GetCalledTask(MultiplexedTransportOperations.Dispose);

        // Shutdown the server to trigger the background client connection shutdown and disposal.
        await server.ShutdownAsync();

        // Act
        ValueTask disposeTask = cache.DisposeAsync();

        // Assert
        await disposedCalledTask;
        using var cts = new CancellationTokenSource(100);
        Assert.That(() => disposeTask.AsTask().WaitAsync(cts.Token), Throws.InstanceOf<OperationCanceledException>());
        clientConnection.Operations.Hold = MultiplexedTransportOperations.None; // Release dispose
        await disposeTask;
    }
}
