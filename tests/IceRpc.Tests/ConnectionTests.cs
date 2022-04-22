// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using IceRpc.Transports.Tests;
using Microsoft.Extensions.DependencyInjection;
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
        [Values(true, false)] bool idleOnClient,
        [Values("ice", "icerpc")] string protocol)
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);
        var tcpServerTransportOptions = new TcpServerTransportOptions
        {
            IdleTimeout = idleOnClient ? TimeSpan.FromHours(1) : TimeSpan.FromMilliseconds(500),
        };

        var tcpClientTransportOptions = new TcpClientTransportOptions
        {
            IdleTimeout = idleOnClient ? TimeSpan.FromMilliseconds(500) : TimeSpan.FromHours(1),
        };

        Connection? serverConnection = null;
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            serverConnection = request.Connection;
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);
        });

        await using var provider = new ConnectionServiceCollection(protocol)
            .UseTcp(tcpServerTransportOptions, tcpClientTransportOptions)
            .UseDispatcher(dispatcher)
            .BuildServiceProvider();

        var server = provider.GetRequiredService<Server>();
        var clientConnection = provider.GetRequiredService<Connection>();
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
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);
        });

        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(dispatcher)
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var connection = provider.GetRequiredService<Connection>();

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
        Connection? serverConnection = null;
        var dispatcher = new InlineDispatcher(async (request, cancel) =>
        {
            serverConnection = request.Connection;
            start.Release();
            await hold.WaitAsync(cancel);
            return new OutgoingResponse(request);
        });

        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(dispatcher)
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var connection = provider.GetRequiredService<Connection>();

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
        Connection? serverConnection = null;
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            serverConnection = request.Connection;
            return new(new OutgoingResponse(request));
        });
        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(dispatcher)
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var clientConnection = provider.GetRequiredService<Connection>();

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
        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))))
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var connection = provider.GetRequiredService<Connection>();

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
        await using var provider = new ConnectionServiceCollection(protocol)
            .UseDispatcher(new InlineDispatcher((request, cancel) => new(new OutgoingResponse(request))))
            .UseConnectionOptions(new ConnectionOptions { IsResumable = true })
            .BuildServiceProvider();
        var server = provider.GetRequiredService<Server>();
        var connection = provider.GetRequiredService<Connection>();

        var proxy = Proxy.FromConnection(connection, "/foo");

        await proxy.Invoker.InvokeAsync(new OutgoingRequest(proxy));
        await connection.ShutdownAsync(default);

        // Act
        var response = await connection.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.IceRpc)), default);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Success));
    }
}

public class ConnectionServiceCollection : ServiceCollection
{
    public ConnectionServiceCollection(string protocol = "icerpc")
    {
        this.UseColoc();
        this.UseSlic();
        this.UseProtocol(protocol == "ice" ? Protocol.Ice : Protocol.IceRpc);
        this.AddScoped(provider =>
        {
            var serverOptions = provider.GetService<ServerOptions>() ?? new ServerOptions();
            if (provider.GetService<IDispatcher>() is IDispatcher dispatcher)
            {
                serverOptions.Dispatcher = dispatcher;
            }
            serverOptions.Endpoint = provider.GetRequiredService<Endpoint>();
            serverOptions.SimpleServerTransport =
                provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();
            serverOptions.MultiplexedServerTransport =
                provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();
            var server = new Server(serverOptions);
            server.Listen();
            return server;
        });

        this.AddScoped(provider =>
        {
            var connectionOptions = provider.GetService<ConnectionOptions>() ?? new ConnectionOptions();
            if (connectionOptions.RemoteEndpoint == null)
            {
                connectionOptions.RemoteEndpoint = provider.GetRequiredService<Server>().Endpoint;
            }
            connectionOptions.SimpleClientTransport =
                provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();
            connectionOptions.MultiplexedClientTransport =
                provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>();
            return new Connection(connectionOptions);
        });
    }
}

public static class ConnectionServiceCollectionExtensions
{
    public static IServiceCollection UseDispatcher(
        this IServiceCollection serviceCollection,
        IDispatcher dispatcher) =>
        serviceCollection.AddScoped(_ => dispatcher);

    public static IServiceCollection UseConnectionOptions(
        this IServiceCollection serviceCollection,
        ConnectionOptions connectionOptions) =>
        serviceCollection.AddScoped(_ => connectionOptions);
}
