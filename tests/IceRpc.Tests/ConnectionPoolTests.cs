// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
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
        var colocTransport = new ColocTransport();
        await using var server1 = new Server(
            new ServerOptions { Endpoint = "icerpc://foo" },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server1.Listen();

        await using var server2 = new Server(
            new ServerOptions { Endpoint = "icerpc://bar" },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server2.Listen();

        await using var pool = new ConnectionPool(
            new ConnectionPoolOptions { PreferExistingConnection = false },
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        ClientConnection connection2 = await pool.GetClientConnectionAsync(
            new EndpointFeature(new ServiceAddress(server2.Endpoint.Protocol) { Endpoint = server2.Endpoint }),
            default);

        var endpointFeature = new EndpointFeature(
            new ServiceAddress(server1.Endpoint.Protocol)
            {
                Endpoint = server1.Endpoint,
                AltEndpoints = ImmutableList.Create(server2.Endpoint)
            });

        // Act
        ClientConnection connection1 = await pool.GetClientConnectionAsync(endpointFeature, default);

        // Assert
        Assert.That(connection1.Endpoint, Is.EqualTo(server1.Endpoint));
        Assert.That(connection1, Is.Not.EqualTo(connection2));
        Assert.That(server1.Endpoint, Is.Not.EqualTo(server2.Endpoint));
    }

    /// <summary>Verifies that the connection pool uses the alt-endpoint when it cannot connect to the main endpoint.
    /// </summary>
    [Test]
    public async Task Get_connection_for_alt_endpoint()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        await using var server = new Server(
            new ServerOptions { Endpoint = "icerpc://foo" },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server.Listen();

        await using var pool = new ConnectionPool(
            new ConnectionPoolOptions() { PreferExistingConnection = true },
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        // Act
        ClientConnection connection = await pool.GetClientConnectionAsync(
            new EndpointFeature(
                new ServiceAddress(Protocol.IceRpc)
                {
                    Endpoint = "icerpc://bar?transport",
                    AltEndpoints = ImmutableList.Create(server.Endpoint)
                }),
            default);

        // Assert
        Assert.That(connection.Endpoint, Is.EqualTo(server.Endpoint));
    }

    /// <summary>Verifies that the connection pool prefers connecting to the main endpoint.</summary>
    [Test]
    public async Task Get_connection_for_main_endpoint()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        await using var server1 = new Server(
            new ServerOptions { Endpoint = "icerpc://foo" },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server1.Listen();

        await using var server2 = new Server(
            new ServerOptions { Endpoint = "icerpc://bar" },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server2.Listen();

        await using var pool = new ConnectionPool(
            new ConnectionPoolOptions(),
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        // Act
        ClientConnection connection = await pool.GetClientConnectionAsync(
            new EndpointFeature(
                new ServiceAddress(server1.Endpoint.Protocol)
                {
                    Endpoint = server1.Endpoint,
                    AltEndpoints = ImmutableList.Create(server2.Endpoint)
                }),
            default);

        // Assert
        Assert.That(connection.Endpoint, Is.EqualTo(server1.Endpoint));
    }

    /// <summary>Verifies that the connection pool reuses existing connection.</summary>
    [Test]
    public async Task Get_connection_reuses_existing_connection()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        await using var server = new Server(
            new ServerOptions { Endpoint = "icerpc://foo", },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server.Listen();

        await using var pool = new ConnectionPool(
            new ConnectionPoolOptions { PreferExistingConnection = true },
            multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        ClientConnection connection1 = await pool.GetClientConnectionAsync(
            new EndpointFeature(new ServiceAddress(server.Endpoint.Protocol) { Endpoint = server.Endpoint }),
            default);

        // Act
        ClientConnection connection2 = await pool.GetClientConnectionAsync(
            new EndpointFeature(new ServiceAddress(server.Endpoint.Protocol) { Endpoint = server.Endpoint }),
            default);

        // Assert
        Assert.That(connection2, Is.EqualTo(connection1));
    }

    /// <summary>Verifies that the connection pool prefers reusing an existing connection when
    /// <c>preferExistingConnection</c> is true.</summary>
    [Test]
    public async Task Prefer_existing_connection()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        await using var server1 = new Server(
            new ServerOptions { Endpoint = "icerpc://foo" },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server1.Listen();

        await using var server2 = new Server(
            new ServerOptions() { Endpoint = "icerpc://bar" },
            multiplexedServerTransport: new SlicServerTransport(colocTransport.ServerTransport));
        server2.Listen();

        await using var pool = new ConnectionPool(
           new ConnectionPoolOptions { PreferExistingConnection = true },
           multiplexedClientTransport: new SlicClientTransport(colocTransport.ClientTransport));

        ClientConnection connection1 = await pool.GetClientConnectionAsync(
            new EndpointFeature(new ServiceAddress(server2.Endpoint.Protocol) { Endpoint = server2.Endpoint }),
            default);

        // Act
        ClientConnection connection2 = await pool.GetClientConnectionAsync(
            new EndpointFeature(
                new ServiceAddress(server2.Endpoint.Protocol)
                {
                    Endpoint = server1.Endpoint,
                    AltEndpoints = ImmutableList.Create(server2.Endpoint)
                }),
            default);

        // Assert
        Assert.That(connection2.Endpoint, Is.EqualTo(server2.Endpoint));
        Assert.That(connection2, Is.EqualTo(connection1));
        Assert.That(server1.Endpoint, Is.Not.EqualTo(server2.Endpoint));
    }
}
