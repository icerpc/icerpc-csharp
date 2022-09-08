// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests;

public sealed class ConnectionCacheTests
{
    /// <summary>Verifies that the connection cache does not prefer existing connections when
    /// <c>preferExistingConnection</c> is false.</summary>
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
        server1.Listen();

        await using var server2 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://bar")),
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server2.Listen();

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
        Assert.Multiple(() =>
        {
            Assert.That(serverAddress?.Host, Is.EqualTo(server1.ServerAddress.Host));
            Assert.That(server1.ServerAddress, Is.Not.EqualTo(server2.ServerAddress));
        });
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
        server.Listen();

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
        await new ServiceProxy(pipeline, new Uri("icerpc://bar/?alt-server=foo")).IcePingAsync();

        // Assert
        Assert.That(serverAddress?.Host, Is.EqualTo(server.ServerAddress.Host));
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
        server1.Listen();

        await using var server2 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://bar"))
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server2.Listen();

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
        Assert.That(serverAddress?.Host, Is.EqualTo(server1.ServerAddress.Host));
    }

    /// <summary>Verifies that the connection cache prefers reusing an existing connection when
    /// <c>preferExistingConnection</c> is true.</summary>
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
        server1.Listen();

        await using var server2 = new Server(
            new ServerOptions()
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                ServerAddress = new ServerAddress(new Uri("icerpc://bar"))
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server2.Listen();

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
        Assert.Multiple(() =>
        {
            Assert.That(serverAddress?.Host, Is.EqualTo(server2.ServerAddress.Host));
            Assert.That(server1.ServerAddress, Is.Not.EqualTo(server2.ServerAddress));
        });
    }
}
