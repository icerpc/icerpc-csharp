// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests;

public sealed class ConnectionPoolTests
{
    /// <summary>Verifies that the connection pool does not prefer existing connections when
    /// <c>preferExistingConnection</c> is false.</summary>
    [Test]
    public async Task Do_not_prefer_existing_connection()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        var colocTransport = new ColocTransport();
        await using var server1 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                Endpoint = "icerpc://foo"
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server1.Listen();

        await using var server2 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                Endpoint = "icerpc://bar",
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server2.Listen();

        await using var pool = new ConnectionPool(
            new ConnectionPoolOptions { PreferExistingConnection = false },
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        Endpoint? endpoint = null;
        Pipeline pipeline = new Pipeline()
            .Use(next => new InlineInvoker(async (request, cancel) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancel);
                    endpoint = request.Features.Get<IEndpointFeature>()?.Endpoint;
                    return response;
                }))
            .Into(pool);

        await ServiceProxy.Parse("icerpc://bar", pool).IcePingAsync();

        // Act
        await ServiceProxy.Parse("icerpc://foo/?alt-endpoint=bar", pipeline).IcePingAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(endpoint?.Host, Is.EqualTo(server1.Endpoint.Host));
            Assert.That(server1.Endpoint, Is.Not.EqualTo(server2.Endpoint));
        });
    }

    /// <summary>Verifies that the connection pool uses the alt-endpoint when it cannot connect to the main endpoint.
    /// </summary>
    [Test]
    public async Task Get_connection_for_alt_endpoint()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        var colocTransport = new ColocTransport();
        await using var server = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                Endpoint = "icerpc://foo"
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server.Listen();

        await using var pool = new ConnectionPool(
            new ConnectionPoolOptions() { PreferExistingConnection = true },
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        Endpoint? endpoint = null;
        Pipeline pipeline = new Pipeline()
            .Use(next => new InlineInvoker(async (request, cancel) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancel);
                    endpoint = request.Features.Get<IEndpointFeature>()?.Endpoint;
                    return response;
                }))
            .Into(pool);

        // Act
        await ServiceProxy.Parse($"icerpc://bar/?alt-endpoint=foo", pipeline).IcePingAsync();

        // Assert
        Assert.That(endpoint?.Host, Is.EqualTo(server.Endpoint.Host));
    }

    /// <summary>Verifies that the connection pool prefers connecting to the main endpoint.</summary>
    [Test]
    public async Task Get_connection_for_main_endpoint()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        var colocTransport = new ColocTransport();
        await using var server1 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                Endpoint = "icerpc://foo"
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server1.Listen();

        await using var server2 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                Endpoint = "icerpc://bar"
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server2.Listen();

        await using var pool = new ConnectionPool(
            new ConnectionPoolOptions(),
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        Endpoint? endpoint = null;
        Pipeline pipeline = new Pipeline()
            .Use(next => new InlineInvoker(async (request, cancel) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancel);
                    endpoint = request.Features.Get<IEndpointFeature>()?.Endpoint;
                    return response;
                }))
            .Into(pool);

        // Act
        await ServiceProxy.Parse($"icerpc://foo/?alt-endpoint=bar", pipeline).IcePingAsync();

        // Assert
        Assert.That(endpoint?.Host, Is.EqualTo(server1.Endpoint.Host));
    }

    /// <summary>Verifies that the connection pool prefers reusing an existing connection when
    /// <c>preferExistingConnection</c> is true.</summary>
    [Test]
    public async Task Prefer_existing_connection()
    {
        // Arrange
        var dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));
        var colocTransport = new ColocTransport();
        await using var server1 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                Endpoint = "icerpc://foo"
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server1.Listen();

        await using var server2 = new Server(
            new ServerOptions()
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher },
                Endpoint = "icerpc://bar"
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server2.Listen();

        await using var pool = new ConnectionPool(
           new ConnectionPoolOptions { PreferExistingConnection = true },
           multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        Endpoint? endpoint = null;
        Pipeline pipeline = new Pipeline()
            .Use(next => new InlineInvoker(async (request, cancel) =>
                {
                    IncomingResponse response = await next.InvokeAsync(request, cancel);
                    endpoint = request.Features.Get<IEndpointFeature>()?.Endpoint;
                    return response;
                }))
            .Into(pool);

        await ServiceProxy.Parse("icerpc://bar", pool).IcePingAsync();

        // Act
        await ServiceProxy.Parse("icerpc://foo/?alt-endpoint=bar", pipeline).IcePingAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(endpoint?.Host, Is.EqualTo(server2.Endpoint.Host));
            Assert.That(server1.Endpoint, Is.Not.EqualTo(server2.Endpoint));
        });
    }
}
