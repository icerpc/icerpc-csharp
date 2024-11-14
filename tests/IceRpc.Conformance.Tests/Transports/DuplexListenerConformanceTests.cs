// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Buffers;
using System.Net;
using System.Net.Security;

namespace IceRpc.Conformance.Tests;

/// <summary>Conformance tests for the duplex transports.</summary>
public abstract class DuplexListenerConformanceTests
{
    [Test]
    public async Task Call_accept_on_a_disposed_listener_fails_with_object_disposed_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        IListener<IDuplexConnection> listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        await listener.DisposeAsync();

        // Act/Assert
        Assert.That(async () => await listener.AcceptAsync(default), Throws.TypeOf<ObjectDisposedException>());
    }

    /// <summary>Verifies that the transport can accept connections.</summary>
    [Test]
    public async Task Call_accept_on_a_listener_accepts_a_connection()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();

        Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> acceptTask = listener.AcceptAsync(default);
        var clientConnectTask = sut.Client.ConnectAsync(default);
        using IDuplexConnection serverConnection = (await acceptTask).Connection;
        var serverConnectTask = serverConnection.ConnectAsync(default);
        await Task.WhenAll(clientConnectTask, serverConnectTask);
    }

    /// <summary>Verifies that connections keep working after the listener is disposed.</summary>
    [Test]
    public async Task Disposing_the_listener_does_not_affect_existing_connections()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();

        await sut.AcceptAndConnectAsync(default);

        // Act
        await sut.Listener.DisposeAsync();

        var payload = new ReadOnlySequence<byte>(new byte[1024]);
        await sut.Client.WriteAsync(payload, default);
        var buffer = new byte[1024];
        var size = await sut.Server.ReadAsync(buffer, default);
        Assert.That(size, Is.EqualTo(1024));
    }

    [Test]
    public async Task Call_accept_on_a_listener_and_then_cancel_the_cancellation_source_fails_with_operation_canceled_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        IListener<IDuplexConnection> listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        using var cancellationSource = new CancellationTokenSource();

        var acceptTask = listener.AcceptAsync(cancellationSource.Token);

        // Act
        cancellationSource.Cancel();

        // Assert
        Assert.That(async () => await acceptTask, Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public async Task Call_accept_on_a_listener_and_then_dispose_it_fails_with_object_disposed_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        IListener<IDuplexConnection> listener = provider.GetRequiredService<IListener<IDuplexConnection>>();

        var acceptTask = listener.AcceptAsync(default);

        // Act
        await listener.DisposeAsync();

        // Assert
        Assert.That(async () => await acceptTask, Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task Call_accept_on_a_listener_with_a_canceled_cancellation_token_fails_with_operation_canceled_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();

        // Act / Assert
        Assert.That(
            async () => await listener.AcceptAsync(new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that connect fails if the listener is disposed.</summary>
    [Test]
    public async Task Connect_fails_if_listener_is_disposed()
    {
        // Arrange

        // We limit the connection backlog to avoid creating too many connections.
        await using ServiceProvider provider = CreateServiceCollection(listenBacklog: 1)
            .BuildServiceProvider(validateScopes: true);
        IListener<IDuplexConnection> listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        var clientTransport = provider.GetRequiredService<IDuplexClientTransport>();

        // A duplex transport listener might use a backlog to accept client connections (e.g.: TCP). So we need to
        // create and establish client connections until a connection establishment blocks to test cancellation.
        Task<TransportConnectionInformation> connectTask;
        var connections = new List<IDuplexConnection>();
        while (true)
        {
            IDuplexConnection? connection = clientTransport.CreateConnection(
                listener.ServerAddress,
                provider.GetService<IOptions<DuplexConnectionOptions>>()?.Value ?? new(),
                clientAuthenticationOptions: provider.GetService<SslClientAuthenticationOptions>());
            connections.Add(connection);
            connectTask = connection.ConnectAsync(default);
            await Task.WhenAny(connectTask, Task.Delay(TimeSpan.FromMilliseconds(250)));
            if (!connectTask.IsCompleted)
            {
                break;
            }
        }

        // Act
        await listener.DisposeAsync();

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await connectTask);

        // If using Ssl and the listener is disposed during the ssl handshake this fails with ConnectionAborted error
        // otherwise it fails with ConnectionRefused.
        Assert.That(
            exception!.IceRpcError,
            Is.EqualTo(IceRpcError.ConnectionRefused).Or.EqualTo(IceRpcError.ConnectionAborted),
            $"The test failed with an unexpected IceRpcError {exception}");

        foreach (IDuplexConnection connection in connections)
        {
            connection.Dispose();
        }
    }

    [Test]
    public async Task Listen_twice_on_the_same_address_fails_with_a_transport_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        var serverTransport = provider.GetRequiredService<IDuplexServerTransport>();

        // Act/Assert
        IceRpcException? exception = Assert.Throws<IceRpcException>(
            () => serverTransport.Listen(listener.ServerAddress, new DuplexConnectionOptions(), null));
        Assert.That(
            exception!.IceRpcError,
            Is.EqualTo(IceRpcError.AddressInUse),
            $"The test failed with an unexpected IceRpcError {exception}");
    }

    [Test]
    public async Task Listener_server_address_transport_property_is_set()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var transport = provider.GetRequiredService<IDuplexClientTransport>().Name;
        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();

        // Act/Assert
        Assert.That(listener.ServerAddress.Transport, Is.EqualTo(transport));
    }

    /// <summary>Creates the service collection used for the duplex listener transport conformance tests.</summary>
    /// <param name="listenBacklog">The length of the server queue for accepting new connections.</param>
    protected abstract IServiceCollection CreateServiceCollection(int? listenBacklog = null);
}
