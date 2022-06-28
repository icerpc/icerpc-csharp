// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Net.Security;

namespace IceRpc.Conformance.Tests;

/// <summary>Conformance tests for the simple transports.</summary>
public abstract class SimpleTransportConformanceTests
{
    /// <summary>Verifies that the transport can accept connections.</summary>
    [Test]
    public async Task Accept_network_connection()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<ISimpleNetworkConnection>>();
        var clientConnection = provider.GetRequiredService<ISimpleNetworkConnection>();

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        _ = clientConnection.ConnectAsync(default);

        // Act/Assert
        Assert.That(
            async () =>
            {
                using ISimpleNetworkConnection _ = await acceptTask;
            },
            Throws.Nothing);
    }

    /// <summary>Write data until the transport flow control starts blocking, at this point we start a read task and
    /// ensure that this unblocks the pending write calls.</summary>
    [Test]
    public async Task Flow_control()
    {
        var payload = new List<ReadOnlyMemory<byte>>() { new byte[1024 * 1024] };
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerSimpleTransportConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<ISimpleNetworkConnection>>(),
            provider.GetRequiredService<ISimpleNetworkConnection>());

        int writtenSize = 0;
        Task writeTask;
        while (true)
        {
            writtenSize += payload[0].Length;
            writeTask = sut.ClientConnection.WriteAsync(payload, default).AsTask();
            await Task.Delay(TimeSpan.FromMilliseconds(100));
            if (writeTask.IsCompleted)
            {
                await writeTask;
            }
            else
            {
                break;
            }
        }

        // Act
        Task readTask = ReadAsync(sut.ServerConnection, writtenSize);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(async () => await writeTask, Throws.Nothing);
            Assert.That(async () => await readTask, Throws.Nothing);
        });

        static async Task ReadAsync(ISimpleNetworkConnection connection, int size)
        {
            var buffer = new byte[1024];
            while (size > 0)
            {
                size -= await connection.ReadAsync(buffer, default);
            }
        }
    }

    [Test]
    public async Task Listen_twice_on_the_same_address_fails_with_a_transport_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<ISimpleNetworkConnection>>();
        var serverAuthenticationOptions = provider.GetService<SslServerAuthenticationOptions>();
        var serverTransport = provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();

        // Act/Assert
        Assert.That(
            () => serverTransport.Listen(listener.Endpoint, serverAuthenticationOptions, NullLogger.Instance),
            Throws.TypeOf<TransportException>());
    }

    [Test]
    public async Task Read_canceled()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<ISimpleNetworkConnection>>();
        using ClientServerSimpleTransportConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<ISimpleNetworkConnection>>(),
            provider.GetRequiredService<ISimpleNetworkConnection>());
        var buffer = new Memory<byte>(new byte[1]);

        Assert.CatchAsync<OperationCanceledException>(
            async () => await sut.ClientConnection.ReadAsync(buffer, new CancellationToken(canceled: true)));
    }

    /// <summary>Verifies that a read operation ends with <see cref="OperationCanceledException"/> if the given
    /// cancellation token is canceled.</summary>
    [Test]
    public async Task Read_cancellation()
    {
        // Arrange
        using var canceled = new CancellationTokenSource();
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerSimpleTransportConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<ISimpleNetworkConnection>>(),
            provider.GetRequiredService<ISimpleNetworkConnection>());
        ValueTask<int> readTask = sut.ClientConnection.ReadAsync(new byte[1], canceled.Token);

        // Act
        canceled.Cancel();

        // Assert
        Assert.That(async () => await readTask, Throws.TypeOf<OperationCanceledException>());
    }

    /// <summary>Verifies that calling read on a connection fails with <see cref="ConnectionLostException"/> if the
    /// peer connection is disposed.</summary>
    [Test]
    public async Task Read_from_disposed_peer_connection_fails_with_connection_lost_exception(
        [Values(true, false)] bool readFromServer)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerSimpleTransportConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<ISimpleNetworkConnection>>(),
            provider.GetRequiredService<ISimpleNetworkConnection>());

        ISimpleNetworkConnection readFrom = readFromServer ? sut.ServerConnection : sut.ClientConnection;
        ISimpleNetworkConnection disposedPeer = readFromServer ? sut.ClientConnection : sut.ServerConnection;

        disposedPeer.Dispose();

        // Act/Assert
        Assert.That(
            async () => await readFrom.ReadAsync(new byte[1], default),
            Throws.TypeOf<ConnectionLostException>());
    }

    /// <summary>Verifies that calling read on a disposed connection fails with <see cref="ObjectDisposedException"/>.
    /// </summary>
    [Test]
    public async Task Read_from_disposed_connection_fails([Values(true, false)] bool disposeServerConnection)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerSimpleTransportConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<ISimpleNetworkConnection>>(),
            provider.GetRequiredService<ISimpleNetworkConnection>());
        ISimpleNetworkConnection disposedConnection = disposeServerConnection ? sut.ServerConnection : sut.ClientConnection;

        disposedConnection.Dispose();

        // Act/Assert
        Assert.That(
            async () => await disposedConnection.ReadAsync(new byte[1], default),
            Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task Read_returns_zero_after_shutdown()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerSimpleTransportConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<ISimpleNetworkConnection>>(),
            provider.GetRequiredService<ISimpleNetworkConnection>());

        // Act
        await sut.ServerConnection.ShutdownAsync(CancellationToken.None);
        int clientRead = await sut.ClientConnection.ReadAsync(new byte[1], CancellationToken.None);

        await sut.ClientConnection.ShutdownAsync(CancellationToken.None);
        int serverRead = await sut.ServerConnection.ReadAsync(new byte[1], CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(clientRead, Is.EqualTo(0));
            Assert.That(serverRead, Is.EqualTo(0));
        });
    }

    [Test]
    public async Task Create_client_connection_with_unknown_endpoint_parameter_fails_with_format_exception()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientTransport = provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();

        Endpoint endpoint = "icerpc://foo?unknown-parameter=foo";

        // Act/Asserts
        Assert.Throws<FormatException>(
            () => clientTransport.CreateConnection(endpoint, authenticationOptions: null, NullLogger.Instance));
    }

    [Test]
    public async Task Create_server_connection_with_unknown_endpoint_parameter_fails_with_format_exception()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var serverTransport = provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();

        Endpoint endpoint = "icerpc://foo?unknown-parameter=foo";

        // Act/Asserts
        Assert.Throws<FormatException>(
            () => serverTransport.Listen(endpoint, authenticationOptions: null, NullLogger.Instance));
    }

    [Test]
    public async Task Write_canceled()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerSimpleTransportConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<ISimpleNetworkConnection>>(),
            provider.GetRequiredService<ISimpleNetworkConnection>());
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1] };

        Assert.CatchAsync<OperationCanceledException>(
            async () => await sut.ClientConnection.WriteAsync(buffer, new CancellationToken(canceled: true)));
    }

    /// <summary>Verifies that pending write operation fails with <see cref="OperationCanceledException"/> once the
    /// cancellation token is canceled.</summary>
    [Test]
    public async Task Write_cancellation()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerSimpleTransportConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<ISimpleNetworkConnection>>(),
            provider.GetRequiredService<ISimpleNetworkConnection>());
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1024 * 1024] };
        using var canceled = new CancellationTokenSource();

        // Write data until flow control blocks the sending. Cancelling the blocked write, should throw
        // OperationCanceledException.
        Task writeTask;
        while (true)
        {
            writeTask = sut.ClientConnection.WriteAsync(buffer, canceled.Token).AsTask();
            await Task.Delay(TimeSpan.FromMilliseconds(20));
            if (writeTask.IsCompleted)
            {
                await writeTask;
            }
            else
            {
                break;
            }
        }

        // Act
        canceled.Cancel();

        // Assert
        Assert.That(async () => await writeTask, Throws.TypeOf<OperationCanceledException>());
    }

    /// <summary>Verifies that calling write fails with <see cref="ConnectionLostException"/> when the peer connection
    /// is disposed.</summary>
    [Test]
    public async Task Write_to_disposed_peer_connection_fails_with_connection_lost_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerSimpleTransportConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<ISimpleNetworkConnection>>(),
            provider.GetRequiredService<ISimpleNetworkConnection>());

        // Act
        sut.ServerConnection.Dispose();

        // Assert
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1] };
        Exception exception;
        try
        {
            // It can take few writes to detect the peer's connection closure.
            while (true)
            {
                await sut.ClientConnection.WriteAsync(buffer, default);
                await Task.Delay(50);
            }
        }
        catch (Exception ex)
        {
            exception = ex;
        }
        Assert.That(exception, Is.InstanceOf<ConnectionLostException>());
    }

    /// <summary>Verifies that calling read on a disposed connection fails with <see cref="ObjectDisposedException"/>.
    /// </summary>
    [Test]
    public async Task Write_to_disposed_connection_fails([Values(true, false)] bool disposeServerConnection)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerSimpleTransportConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<ISimpleNetworkConnection>>(),
            provider.GetRequiredService<ISimpleNetworkConnection>());
        ISimpleNetworkConnection disposedConnection =
            disposeServerConnection ? sut.ServerConnection : sut.ClientConnection;

        disposedConnection.Dispose();

        // Act/Assert
        Assert.That(
            async () => await disposedConnection.WriteAsync(
                new List<ReadOnlyMemory<byte>>() { new byte[1024] },
                default),
            Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task Write_fails_after_shutdown()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerSimpleTransportConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<ISimpleNetworkConnection>>(),
            provider.GetRequiredService<ISimpleNetworkConnection>());
        await sut.ServerConnection.ShutdownAsync(CancellationToken.None);

        // Act/Assert
        Assert.CatchAsync<Exception>(async () =>
            await sut.ServerConnection.WriteAsync(new List<ReadOnlyMemory<byte>> { new byte[1] },
            CancellationToken.None));
    }

    /// <summary>Verifies that we can write and read using server and client connections.</summary>
    [Test]
    public async Task WriteAndRead(
        [Values(1, 1024, 16 * 1024, 32 * 1024, 64 * 1024, 1024 * 1024)] int size,
        [Values(true, false)] bool useServerConnection)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerSimpleTransportConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<ISimpleNetworkConnection>>(),
            provider.GetRequiredService<ISimpleNetworkConnection>());
        byte[] writeBuffer = Enumerable.Range(0, size).Select(i => (byte)(i % 255)).ToArray();

        ISimpleNetworkConnection writeConnection = useServerConnection ? sut.ServerConnection : sut.ClientConnection;
        ISimpleNetworkConnection readConnection = useServerConnection ? sut.ClientConnection : sut.ServerConnection;

        // Act
        ValueTask writeTask = writeConnection.WriteAsync(new ReadOnlyMemory<byte>[] { writeBuffer }, default);

        // Assert
        Memory<byte> readBuffer = new byte[size];
        int offset = 0;
        while (offset < size)
        {
            offset += await readConnection.ReadAsync(readBuffer[offset..], default);
        }
        await writeTask;
        Assert.Multiple(() =>
        {
            Assert.That(offset, Is.EqualTo(size));
            Assert.That(readBuffer.Span.SequenceEqual(writeBuffer), Is.True);
        });
    }

    /// <summary>Creates the service collection used for the simple transport conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();

    private static async Task<ClientServerSimpleTransportConnection> ConnectAndAcceptAsync(
        IListener<ISimpleNetworkConnection> listener,
        ISimpleNetworkConnection clientConnection)
    {
        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        Task<INetworkConnectionInformationFeature> clientConnectTask = clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        Task<INetworkConnectionInformationFeature> serverConnectTask = serverConnection.ConnectAsync(default);
        await Task.WhenAll(clientConnectTask, serverConnectTask);
        return new ClientServerSimpleTransportConnection(clientConnection, serverConnection);
    }
}

public record struct ClientServerSimpleTransportConnection : IDisposable
{
    public ISimpleNetworkConnection ClientConnection { get; }
    public ISimpleNetworkConnection ServerConnection { get; }

    public ClientServerSimpleTransportConnection(
        ISimpleNetworkConnection clientConnection,
        ISimpleNetworkConnection serverConnection)
    {
        ClientConnection = clientConnection;
        ServerConnection = serverConnection;
    }

    public void Dispose()
    {
        ClientConnection.Dispose();
        ServerConnection.Dispose();
    }
}
