// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
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

        IConnection connection2 = await pool.GetConnectionAsync(
            server2.Endpoint,
            Array.Empty<Endpoint>(),
            default);

        // Act
        IConnection connection1 = await pool.GetConnectionAsync(
            server1.Endpoint,
            new Endpoint[] { server2.Endpoint },
            default);

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
        IConnection connection = await pool.GetConnectionAsync(
            Endpoint.FromString("icerpc://bar?transport=coloc"),
            new Endpoint[] { server.Endpoint },
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
        IConnection connection = await pool.GetConnectionAsync(
            server1.Endpoint,
            new Endpoint[] { server2.Endpoint },
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

        IConnection connection1 = await pool.GetConnectionAsync(
            server.Endpoint,
            Array.Empty<Endpoint>(),
            default);

        // Act
        IConnection connection2 = await pool.GetConnectionAsync(
            server.Endpoint,
            Array.Empty<Endpoint>(),
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

        IConnection connection1 = await pool.GetConnectionAsync(
            server2.Endpoint,
            Array.Empty<Endpoint>(),
            default);

        // Act
        IConnection connection2 = await pool.GetConnectionAsync(
            server1.Endpoint,
            new Endpoint[] { server2.Endpoint },
            default);

        // Assert
        Assert.That(connection2.Endpoint, Is.EqualTo(server2.Endpoint));
        Assert.That(connection2, Is.EqualTo(connection1));
        Assert.That(server1.Endpoint, Is.Not.EqualTo(server2.Endpoint));
    }
}
