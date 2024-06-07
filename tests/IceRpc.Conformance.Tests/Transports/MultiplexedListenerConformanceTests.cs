// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.IO.Pipelines;
using System.Net.Security;

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

    /// <summary>Verifies that connections keep working after the listener is disposed.</summary>
    [Test]
    public async Task Disposing_the_listener_does_not_affect_existing_connections()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();
        byte[] buffer = new byte[1024];

        // Act
        await clientServerConnection.Listener.DisposeAsync();

        // Assert
        Task writeTask = WriteDataAsync();
        Assert.That(async () => await ReadDataAsync(), Is.EqualTo(buffer.Length));
        Assert.That(() => writeTask, Throws.Nothing);

        async Task<int> ReadDataAsync()
        {
            ReadResult readResult;
            int readLength = 0;
            do
            {
                readResult = await sut.Remote.Input.ReadAsync(default);
                readLength += (int)readResult.Buffer.Length;
                sut.Remote.Input.AdvanceTo(readResult.Buffer.End);
            }
            while (!readResult.IsCompleted);
            return readLength;
        }

        async Task WriteDataAsync()
        {
            // Send a large buffer to ensure the transport (eventually) buffers the sending of the data.
            await sut.Local.Output.WriteAsync(buffer);

            // Act
            sut.Local.Output.Complete();
        }
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
    [Ignore("TODO: Fix https://github.com/icerpc/icerpc-csharp/issues/3990")]
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

        // Ensure the listener is accepting connections
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        await using IMultiplexedConnection connection = clientTransport.CreateConnection(
            listener.ServerAddress,
            provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
            provider.GetService<SslClientAuthenticationOptions>());

        // Act
        await listener.DisposeAsync();

        // With QUIC, disposing of the first connection before the second connection attempt ensures a timely failure.
        await sut.DisposeAsync();

        // Assert
        Assert.That(
            async () => await connection.ConnectAsync(default),
            Throws.InstanceOf<IceRpcException>());
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
        var transport = provider.GetRequiredService<IMultiplexedClientTransport>().Name;
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Act/Assert
        Assert.That(listener.ServerAddress.Transport, Is.EqualTo(transport));
    }

    /// <summary>Creates the service collection used for multiplexed listener conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();
}
