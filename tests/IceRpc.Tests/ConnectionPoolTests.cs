// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Transports;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests;

public sealed class ConnectionPoolTests
{
    /// <summary>Verifies that the connection pool does not prefer existing connections when
    /// <c>preferExistingConnection</c> is false.</summary>
    [Test]
    public async Task Do_not_prefer_existing_connection()
    {
        // Arrange
        TaskCompletionSource<Endpoint?> tcs = new();
        var dispatcher1 = new InlineDispatcher((request, cancel) =>
            {
                tcs.SetResult(request.Features.Get<IEndpointFeature>()?.Endpoint);
                return new(new OutgoingResponse(request));
            });
        var dispatcher2 = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));

        var colocTransport = new ColocTransport();
        await using var server1 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher1 },
                Endpoint = "icerpc://foo"
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server1.Listen();

        await using var server2 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher2 },
                Endpoint = "icerpc://bar",
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server2.Listen();

        await using var pool = new ConnectionPool(
            new ConnectionPoolOptions { PreferExistingConnection = false },
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        await ServiceProxy.Parse("icerpc://bar", pool).IcePingAsync();

        // Act
        await ServiceProxy.Parse("icerpc://foo/?alt-endpoint=bar", pool).IcePingAsync();

        // Assert
        Endpoint? endpoint = await tcs.Task;
        Assert.Multiple(() =>
        {
            Assert.That(endpoint, Is.EqualTo(server1.Endpoint));
            Assert.That(server1.Endpoint, Is.Not.EqualTo(server2.Endpoint));
        });
    }

    /// <summary>Verifies that the connection pool uses the alt-endpoint when it cannot connect to the main endpoint.
    /// </summary>
    [Test]
    public async Task Get_connection_for_alt_endpoint()
    {
        // Arrange
        TaskCompletionSource<Endpoint?> tcs = new();
        var dispatcher = new InlineDispatcher((request, cancel) =>
            {
                tcs.SetResult(request.Features.Get<IEndpointFeature>()?.Endpoint);
                return new(new OutgoingResponse(request));
            });

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

        // Act
        await ServiceProxy.Parse($"icerpc://bar?transport/?alt-endpoint={server.Endpoint}").IcePingAsync();

        // Assert
        Endpoint? endpoint = await tcs.Task;
        Assert.That(endpoint, Is.EqualTo(server.Endpoint));
    }

    /// <summary>Verifies that the connection pool prefers connecting to the main endpoint.</summary>
    [Test]
    public async Task Get_connection_for_main_endpoint()
    {
        // Arrange

        TaskCompletionSource<Endpoint?> tcs = new();
        var dispatcher = new InlineDispatcher((request, cancel) =>
            {
                tcs.SetResult(request.Features.Get<IEndpointFeature>()?.Endpoint);
                return new(new OutgoingResponse(request));
            });

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

        // Act
        await ServiceProxy.Parse($"icerpc://{server1.Endpoint}/?alt-endpoints={server2.Endpoint}").IcePingAsync();

        // Assert
        Endpoint? endpoint = await tcs.Task;
        Assert.That(endpoint, Is.EqualTo(server1.Endpoint));
    }

    /// <summary>Verifies that the connection pool prefers reusing an existing connection when
    /// <c>preferExistingConnection</c> is true.</summary>
    [Test]
    public async Task Prefer_existing_connection()
    {
        // Arrange
        TaskCompletionSource<Endpoint?> tcs = new();
        var dispatcher1 = new InlineDispatcher((request, cancel) =>
            {
                tcs.SetResult(request.Features.Get<IEndpointFeature>()?.Endpoint);
                return new(new OutgoingResponse(request));
            });
        var dispatcher2 = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request)));

        var colocTransport = new ColocTransport();
        await using var server1 = new Server(
            new ServerOptions
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher1 },
                Endpoint = "icerpc://foo"
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server1.Listen();

        await using var server2 = new Server(
            new ServerOptions()
            {
                ConnectionOptions = new ConnectionOptions { Dispatcher = dispatcher2 },
                Endpoint = "icerpc://bar"
            },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server2.Listen();

        await using var pool = new ConnectionPool(
           new ConnectionPoolOptions { PreferExistingConnection = true },
           multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        await ServiceProxy.Parse("icerpc://bar", pool).IcePingAsync();

        // Act
        await ServiceProxy.Parse("icerpc://foo/?alt-endpoint=bar", pool).IcePingAsync();

        // Assert
        Endpoint? endpoint = await tcs.Task;
        Assert.Multiple(() =>
        {
            Assert.That(endpoint, Is.EqualTo(server2.Endpoint));
            Assert.That(server1.Endpoint, Is.Not.EqualTo(server2.Endpoint));
        });
    }
}
