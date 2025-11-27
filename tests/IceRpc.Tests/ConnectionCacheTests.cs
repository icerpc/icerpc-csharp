// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Internal;
using IceRpc.Tests.Common;
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

        await SendEmptyRequestAsync(cache, new ServiceAddress(new Uri("icerpc://bar")));

        // Act
        await SendEmptyRequestAsync(pipeline, new ServiceAddress(new Uri("icerpc://foo/?alt-server=bar")));

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
        await SendEmptyRequestAsync(pipeline, new ServiceAddress(new Uri("icerpc://bar/?alt-server=foo")));

        // Assert
        Assert.That(selectedServerAddress?.Host, Is.EqualTo(serverAddress.Host));

        // Cleanup
        await server.ShutdownAsync();
        await cache.ShutdownAsync();
    }

    /// <summary>Verifies that the connection cache uses the alt-server when the primary server is busy.</summary>
    [TestCase("icerpc")]
    [TestCase("ice")]
    public async Task Get_connection_when_primary_server_busy(string protocolString)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();
        var colocTransport = new ColocTransport();
        var protocol = Protocol.Parse(protocolString);
        var primaryServerAddress = new ServerAddress(protocol) { Host = "primary" };
        var altServerAddress = new ServerAddress(protocol) { Host = "alt" };

        await using var primaryServer = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                MaxConnections = 1,
                ServerAddress = primaryServerAddress
            },
            duplexServerTransport: colocTransport.ServerTransport,
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        _ = primaryServer.Listen();

        await using var altServer = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = altServerAddress
            },
            duplexServerTransport: colocTransport.ServerTransport,
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        _ = altServer.Listen();

        // Make primary busy by using the only connection it accepts
        await using var clientConnection = new ClientConnection(
            primaryServerAddress,
            duplexClientTransport: colocTransport.ClientTransport,
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));
        await clientConnection.ConnectAsync();

        await using var cache = new ConnectionCache(
            new ConnectionCacheOptions(),
            duplexClientTransport: colocTransport.ClientTransport,
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
        await SendEmptyRequestAsync(
            pipeline,
            new ServiceAddress(protocol)
            {
                ServerAddress = primaryServerAddress,
                AltServerAddresses = [altServerAddress]
            });

        // Assert
        Assert.That(selectedServerAddress, Is.EqualTo(altServerAddress));

        // Cleanup
        await primaryServer.ShutdownAsync();
        await altServer.ShutdownAsync();
        await clientConnection.ShutdownAsync();
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
        await SendEmptyRequestAsync(pipeline, new ServiceAddress(new Uri("icerpc://foo/?alt-server=bar")));

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

        await SendEmptyRequestAsync(cache, new ServiceAddress(new Uri("icerpc://bar")));

        // Act
        await SendEmptyRequestAsync(pipeline, new ServiceAddress(new Uri("icerpc://foo/?alt-server=bar")));

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
        await SendEmptyRequestAsync(cache, new ServiceAddress(new Uri("icerpc://foo")));

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

    /// <summary>Verifies that the ConnectionCacheOptions.ConnectTimeout is transmitted to the multiplexed transport as
    /// HandshakeTimeout.</summary>
    [Test]
    public async Task Connect_timeout_is_transmitted_as_handshake_timeout_to_multiplexed_transport()
    {
        // Arrange
        var connectTimeout = TimeSpan.FromSeconds(42);
        using var dispatcher = new TestDispatcher();
        var colocTransport = new ColocTransport();
        var multiplexedClientTransport = new TestMultiplexedClientTransportDecorator(
            new SlicClientTransport(colocTransport.ClientTransport));

        await using var server = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://foo"))
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server.Listen();

        await using var cache = new ConnectionCache(
            new ConnectionCacheOptions { ConnectTimeout = connectTimeout },
            multiplexedClientTransport: multiplexedClientTransport);

        // Act
        await SendEmptyRequestAsync(cache, new ServiceAddress(new Uri("icerpc://foo")));

        // Assert
        Assert.That(multiplexedClientTransport.LastCreatedConnectionOptions, Is.Not.Null);
        Assert.That(multiplexedClientTransport.LastCreatedConnectionOptions!.HandshakeTimeout, Is.EqualTo(connectTimeout));

        // Cleanup
        await server.ShutdownAsync();
        await cache.ShutdownAsync();
    }

    private static async Task SendEmptyRequestAsync(IInvoker invoker, ServiceAddress serviceAddress)
    {
        using var request = new OutgoingRequest(serviceAddress)
        {
            Payload = EmptyPipeReader.Instance
        };
        await invoker.InvokeAsync(request, CancellationToken.None);
    }
}
