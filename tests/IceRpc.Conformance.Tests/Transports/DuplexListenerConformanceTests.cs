// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
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
    public async Task Call_accept_on_a_listener_and_then_dispose_it_fails_with_operation_aborted_error()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        IListener<IDuplexConnection> listener = provider.GetRequiredService<IListener<IDuplexConnection>>();

        var acceptTask = listener.AcceptAsync(default);

        // Act
        await listener.DisposeAsync();

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await acceptTask);
        Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.OperationAborted));
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
        IDuplexConnection clientConnection;
        while (true)
        {
            IDuplexConnection? connection = clientTransport.CreateConnection(
                listener.ServerAddress,
                provider.GetService<IOptions<DuplexConnectionOptions>>()?.Value ?? new(),
                clientAuthenticationOptions: provider.GetService<SslClientAuthenticationOptions>());
            try
            {
                connectTask = connection.ConnectAsync(default);
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                if (connectTask.IsCompleted)
                {
                    await connectTask;
                }
                else
                {
                    clientConnection = connection;
                    connection = null;
                    break;
                }
            }
            finally
            {
                connection?.Dispose();
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
            Is.EqualTo(IceRpcError.ConnectionRefused).Or.EqualTo(IceRpcError.ConnectionAborted));
        clientConnection.Dispose();
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
        Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.AddressInUse));
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
