// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
[Timeout(5000)]
public class ConnectionTests
{
    /// <summary>Verifies that closing the connection abort the invocations.</summary>
    [Test]
    public async Task Close_on_idle(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool idleOnClient)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        var tcpServerTransport = new TcpServerTransport(new TcpServerTransportOptions
        {
            IdleTimeout = idleOnClient ? TimeSpan.FromHours(1) : TimeSpan.FromMilliseconds(500),
        });

        var tcpClientTransport = new TcpClientTransport(new TcpClientTransportOptions
        {
            IdleTimeout = idleOnClient ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromHours(1),
        });

        Connection? serverConnection = null;
        await using var server = new Server(new ServerOptions
        {
            Dispatcher = new InlineDispatcher(
                async (request, cancel) =>
                {
                    serverConnection = request.Connection;
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                }),
            Endpoint = $"{protocol}://127.0.0.1:0",
            SimpleServerTransport = tcpServerTransport,
            MultiplexedServerTransport = new SlicServerTransport(tcpServerTransport),
        });
        server.Listen();
        await using var clientConnection = new Connection(new ConnectionOptions
        {
            RemoteEndpoint = server.Endpoint,
            SimpleClientTransport = tcpClientTransport,
            MultiplexedClientTransport = new SlicClientTransport(tcpClientTransport)
        });
        var proxy = Proxy.FromConnection(clientConnection, "/foo");

        var invokeTask = proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        await start.WaitAsync();

        var serverConnectionClosed = new TaskCompletionSource<object?>();
        serverConnection!.Closed += (sender, args) => serverConnectionClosed.SetResult(null);

        var clientConnectionClosed = new TaskCompletionSource<object?>();
        clientConnection!.Closed += (sender, args) => clientConnectionClosed.SetResult(null);

        // Act
        hold.Release(); // let the connection iddle

        // Assert
        Assert.That(async () => await serverConnectionClosed.Task, Throws.Nothing);
        Assert.That(async () => await clientConnectionClosed.Task, Throws.Nothing);
    }

    /// <summary>Verifies that closing the connection abort the invocations.</summary>
    [Test]
    public async Task Closing_the_connection_cancels_the_invocation([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        var coloc = new ColocTransport();
        await using var server = new Server(new ServerOptions
        {
            Dispatcher = new InlineDispatcher(
                async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                }),
            Endpoint = $"{protocol}://{Guid.NewGuid()}?transport=coloc",
            MultiplexedServerTransport = new SlicServerTransport(coloc.ServerTransport),
            SimpleServerTransport = coloc.ServerTransport,
        });
        server.Listen();
        await using var connection = new Connection(new ConnectionOptions
        {
            RemoteEndpoint = server.Endpoint,
            MultiplexedClientTransport = new SlicClientTransport(coloc.ClientTransport),
            SimpleClientTransport = coloc.ClientTransport,
        });

        var proxy = Proxy.FromConnection(connection, "/foo");

        var invokeTask = proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        await start.WaitAsync(); // Wait for dispatch to start

        // Act
        await connection.CloseAsync();

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ObjectDisposedException>());
    }

    /// <summary>Verifies that closing the connection abort the invocations.</summary>
    [Test]
    public async Task Closing_the_connection_cancels_the_dispatches([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        var coloc = new ColocTransport();
        Connection? serverConnection = null;
        await using var server = new Server(new ServerOptions
        {
            Dispatcher = new InlineDispatcher(
                async (request, cancel) =>
                {
                    serverConnection = request.Connection;
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                }),
            Endpoint = $"{protocol}://{Guid.NewGuid()}?transport=coloc",
            MultiplexedServerTransport = new SlicServerTransport(coloc.ServerTransport),
            SimpleServerTransport = coloc.ServerTransport,
        });
        server.Listen();
        await using var connection = new Connection(new ConnectionOptions
        {
            RemoteEndpoint = server.Endpoint,
            MultiplexedClientTransport = new SlicClientTransport(coloc.ClientTransport),
            SimpleClientTransport = coloc.ClientTransport,
        });

        var proxy = Proxy.FromConnection(connection, "/foo");

        var invokeTask = proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        await start.WaitAsync(); // Wait for dispatch to start

        // Act
        await serverConnection!.CloseAsync();

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<ConnectionLostException>());
    }

    /// <summary>Verifies that closing the connection raises the connection closed event.</summary>
    [Test]
    public async Task Connection_closed_event(
        [Values("ice", "icerpc")] string protocol,
        [Values(true, false)] bool closeClientConnection)
    {
        // Arrange
        var coloc = new ColocTransport();
        Connection? serverConnection = null;
        await using var server = new Server(new ServerOptions
        {
            Dispatcher = new InlineDispatcher(
                (request, cancel) =>
                {
                    serverConnection = request.Connection;
                    return new(new OutgoingResponse(request));
                }),
            Endpoint = $"{protocol}://{Guid.NewGuid()}?transport=coloc",
            MultiplexedServerTransport = new SlicServerTransport(coloc.ServerTransport),
            SimpleServerTransport = coloc.ServerTransport,
        });
        server.Listen();
        await using var clientConnection = new Connection(new ConnectionOptions
        {
            RemoteEndpoint = server.Endpoint,
            MultiplexedClientTransport = new SlicClientTransport(coloc.ClientTransport),
            SimpleClientTransport = coloc.ClientTransport,
        });

        var proxy = Proxy.FromConnection(clientConnection, "/foo");

        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        var serverConnectionClosed = new TaskCompletionSource<object?>();
        serverConnection!.Closed += (sender, args) => serverConnectionClosed.SetResult(null);

        var clientConnectionClosed = new TaskCompletionSource<object?>();
        clientConnection!.Closed += (sender, args) => clientConnectionClosed.SetResult(null);

        // Act
        await (closeClientConnection ? clientConnection : serverConnection!).CloseAsync();

        // Assert
        Assert.That(async () => await serverConnectionClosed.Task, Throws.Nothing);
        Assert.That(async () => await clientConnectionClosed.Task, Throws.Nothing);
    }

    /// <summary>Verifies that closing the connection abort the invocations.</summary>
    [Test]
    public async Task Connect_timeout([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        var tcpServerTransport = new TcpServerTransport();
        var slicServerTransport = new SlicServerTransport(tcpServerTransport);

        var listener = slicServerTransport.Listen($"{protocol}://127.0.0.1:0", null, NullLogger.Instance);
        await using var connection = new Connection(new ConnectionOptions
        {
            RemoteEndpoint = listener.Endpoint,
            ConnectTimeout = TimeSpan.FromMilliseconds(100),
        });

        // Assert
        Assert.That(async () => await connection.ConnectAsync(default), Throws.TypeOf<ConnectTimeoutException>());
    }


    [Test]
    public async Task Non_resumable_connection_cannot_reconnect([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        var coloc = new ColocTransport();
        await using var server = new Server(new ServerOptions
        {
            Dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))),
            Endpoint = $"{protocol}://{Guid.NewGuid()}?transport=coloc",
            MultiplexedServerTransport = new SlicServerTransport(coloc.ServerTransport),
            SimpleServerTransport = coloc.ServerTransport,
        });
        server.Listen();
        await using var connection = new Connection(new ConnectionOptions
        {
            IsResumable = false,
            RemoteEndpoint = server.Endpoint,
            MultiplexedClientTransport = new SlicClientTransport(coloc.ClientTransport),
            SimpleClientTransport = coloc.ClientTransport,
        });

        var proxy = Proxy.FromConnection(connection, "/foo");

        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        await connection.ShutdownAsync(default);

        // Act/Assert
        Assert.That(
            async () => await connection.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)), default),
            Throws.TypeOf<ConnectionClosedException>());
    }

    [Test]
    public async Task Resumable_connection_can_reconnect([Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        var coloc = new ColocTransport();
        await using var server = new Server(new ServerOptions
        {
            Dispatcher = new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))),
            Endpoint = $"{protocol}://{Guid.NewGuid()}?transport=coloc",
            MultiplexedServerTransport = new SlicServerTransport(coloc.ServerTransport),
            SimpleServerTransport = coloc.ServerTransport,
        });
        server.Listen();
        await using var connection = new Connection(new ConnectionOptions
        {
            IsResumable = true,
            RemoteEndpoint = server.Endpoint,
            MultiplexedClientTransport = new SlicClientTransport(coloc.ClientTransport),
            SimpleClientTransport = coloc.ClientTransport,
        });

        var proxy = Proxy.FromConnection(connection, "/foo");

        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        await connection.ShutdownAsync(default);

        // Act
        var response = await connection.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)), default);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
    }
}
