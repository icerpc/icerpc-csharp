// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Net.Security;
using System.Security.Authentication;

namespace IceRpc.Conformance.Tests;

/// <summary>Conformance tests for the multiplexed transports.</summary>
public abstract class MultiplexedListenerConformanceTests
{
    [Test]
    public async Task Call_accept_on_a_disposed_listener_fails_with_object_disposed_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await listener.DisposeAsync();

        // Act/Assert
        Assert.That(async () => await listener.AcceptAsync(default), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task Call_accept_on_a_listener_accepts_a_connection()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        IListener<IMultiplexedConnection> listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
        await using var clientConnection = clientTransport.CreateConnection(
            listener.ServerAddress,
            provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
            provider.GetService<SslClientAuthenticationOptions>());

        var acceptTask = listener.AcceptAsync(default);
        var clientConnectTask = clientConnection.ConnectAsync(default);

        // Act/Assert
        Assert.That(
            async () =>
            {
                await using IMultiplexedConnection serverConnection = (await acceptTask).Connection;
                await serverConnection.ConnectAsync(default);
                await clientConnectTask;
            },
            Throws.Nothing);
    }

    [Test]
    public async Task Call_accept_on_a_listener_and_then_cancel_the_cancellation_source_fails_with_operation_canceled_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
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
        IListener<IMultiplexedConnection> listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        var acceptTask = listener.AcceptAsync(default);

        // Act
        await listener.DisposeAsync();

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await acceptTask);
        Assert.That(
            exception!.IceRpcError,
            Is.EqualTo(IceRpcError.OperationAborted),
            $"The test failed with an unexpected IceRpcError {exception}");
    }

    [Test]
    public async Task Call_accept_on_a_listener_with_a_canceled_cancellation_token_fails_with_operation_canceled_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

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
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        IListener<IMultiplexedConnection> listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();

        Task connectTask = ConnectAsync(clientTransport);

        // Act
        await listener.DisposeAsync();

        // Assert

        // If using Quic and the listener is disposed during the ssl handshake this can fail with
        // AuthenticationException otherwise it fails with IceRpcException.
        Assert.That(
            async () => await connectTask,
            Throws.InstanceOf<IceRpcException>().Or.TypeOf<AuthenticationException>());

        async Task ConnectAsync(IMultiplexedClientTransport clientTransport)
        {
            // Establish connections until we get a failure.
            var connections = new List<IMultiplexedConnection>();
            try
            {
                while (true)
                {
                    IMultiplexedConnection connection = clientTransport.CreateConnection(
                        listener.ServerAddress,
                        provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                        provider.GetService<SslClientAuthenticationOptions>());
                    connections.Add(connection);

                    await connection.ConnectAsync(default);

                    // Continue until connect fails.
                }
            }
            finally
            {
                await Task.WhenAll(connections.Select(c => c.DisposeAsync().AsTask()));
            }
        }
    }

    [Test]
    public async Task Listen_twice_on_the_same_address_fails_with_a_transport_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var serverTransport = provider.GetRequiredService<IMultiplexedServerTransport>();

        // Act/Assert
        IceRpcException? exception = Assert.Throws<IceRpcException>(
            () => serverTransport.Listen(
                listener.ServerAddress,
                new MultiplexedConnectionOptions(),
                provider.GetService<SslServerAuthenticationOptions>()));
        // BUGFIX with Quic this throws an internal error https://github.com/dotnet/runtime/issues/78573
        Assert.That(
            exception!.IceRpcError,
            Is.EqualTo(IceRpcError.AddressInUse).Or.EqualTo(IceRpcError.IceRpcError),
            $"The test failed with an unexpected IceRpcError {exception}");
    }

    [Test]
    public async Task Listener_server_address_transport_property_is_set()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var transport = provider.GetRequiredService<IMultiplexedClientTransport>().Name;
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Act/Assert
        Assert.That(listener.ServerAddress.Transport, Is.EqualTo(transport));
    }

    /// <summary>Creates the service collection used for multiplexed listener conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();
}
