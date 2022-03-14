// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
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
        await using var server1 = new Server(ConnectionOptions.DefaultDispatcher, "icerpc://127.0.0.1:0");
        server1.Listen();

        await using var server2 = new Server(ConnectionOptions.DefaultDispatcher, "icerpc://127.0.0.1:0");
        server2.Listen();

        await using var pool = new ConnectionPool(new ConnectionOptions(), preferExistingConnection: false);

        Connection connection = await pool.GetConnectionAsync(
            server2.Endpoint,
            Array.Empty<Endpoint>(),
            default);

        // Act
        connection = await pool.GetConnectionAsync(
            server1.Endpoint,
            new Endpoint[] { server2.Endpoint },
            default);

        // Assert
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Active));
        Assert.That(connection.RemoteEndpoint, Is.EqualTo(server1.Endpoint));
        Assert.That(server1.Endpoint, Is.Not.EqualTo(server2.Endpoint));
    }

    /// <summary>Verifies that the connection pool uses the alt-endpoint when it cannot connect to the main endpoint.
    /// </summary>
    [Test]
    public async Task Get_connection_for_alt_endpoint()
    {
        // Arrange
        await using var server = new Server(ConnectionOptions.DefaultDispatcher, "icerpc://127.0.0.1:0");
        server.Listen();

        await using var pool = new ConnectionPool(new ConnectionOptions(), preferExistingConnection: true);

        // Act
        Connection connection = await pool.GetConnectionAsync(
            Endpoint.FromString("icerpc://127.0.0.1:10000"),
            new Endpoint[] { server.Endpoint },
            default);

        // Assert
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Active));
        Assert.That(connection.RemoteEndpoint, Is.EqualTo(server.Endpoint));
    }

    /// <summary>Verifies that the connection pool prefers connecting to the main endpoint.</summary>
    [Test]
    public async Task Get_connection_for_main_endpoint()
    {
        // Arrange
        await using var server1 = new Server(ConnectionOptions.DefaultDispatcher, "icerpc://127.0.0.1:0");
        server1.Listen();

        await using var server2 = new Server(ConnectionOptions.DefaultDispatcher, "icerpc://127.0.0.1:0");
        server2.Listen();

        await using var pool = new ConnectionPool(new ConnectionOptions(), preferExistingConnection: true);

        // Act
        Connection connection = await pool.GetConnectionAsync(
            server1.Endpoint,
            new Endpoint[] { server2.Endpoint },
            default);

        // Assert
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Active));
        Assert.That(connection.RemoteEndpoint, Is.EqualTo(server1.Endpoint));
    }

    /// <summary>Verifies that the connection pool reuse existing connection.</summary>
    [Test]
    public async Task Get_connection_reuse_existing_connection()
    {
        // Arrange
        await using var server1 = new Server(ConnectionOptions.DefaultDispatcher, "icerpc://127.0.0.1:0");
        server1.Listen();

        await using var server2 = new Server(ConnectionOptions.DefaultDispatcher, "icerpc://127.0.0.1:0");
        server2.Listen();

        await using var pool = new ConnectionPool(new ConnectionOptions(), preferExistingConnection: false);

        Connection connection1 = await pool.GetConnectionAsync(
            server1.Endpoint,
            new Endpoint[] { server2.Endpoint },
            default);

        // Act
        Connection connection2 = await pool.GetConnectionAsync(
            server1.Endpoint,
            new Endpoint[] { server2.Endpoint },
            default);

        // Assert
        Assert.That(connection2.State, Is.EqualTo(ConnectionState.Active));
        Assert.That(connection2, Is.EqualTo(connection1));
    }

    /// <summary>Verifies that the connection pool prefers reusing an existing connection when
    /// <c>preferExistingConnection</c> is used.</summary>
    [Test]
    public async Task Prefer_existing_connection()
    {
        // Arrange
        await using var server1 = new Server(ConnectionOptions.DefaultDispatcher, "icerpc://127.0.0.1:0");
        server1.Listen();

        await using var server2 = new Server(ConnectionOptions.DefaultDispatcher, "icerpc://127.0.0.1:0");
        server2.Listen();

        await using var pool = new ConnectionPool(new ConnectionOptions(), preferExistingConnection: true);

        Connection connection = await pool.GetConnectionAsync(
            server2.Endpoint,
            Array.Empty<Endpoint>(),
            default);

        // Act
        connection = await pool.GetConnectionAsync(
            server1.Endpoint,
            new Endpoint[] { server2.Endpoint },
            default);


        // Assert
        Assert.That(connection.State, Is.EqualTo(ConnectionState.Active));
        Assert.That(connection.RemoteEndpoint, Is.EqualTo(server2.Endpoint));
        Assert.That(server1.Endpoint, Is.Not.EqualTo(server2.Endpoint));
    }
}
