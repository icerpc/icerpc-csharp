// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.Net.Security;

namespace IceRpc.Transports.Tests;

/// <summary>Conformance tests for the simple transports.</summary>
public abstract class SimpleTransportConformanceTests
{
    /// <summary>Verifies that the transport can accept connections.</summary>
    [Test]
    public async Task Accept_network_connection()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using IListener<ISimpleNetworkConnection> listener = provider.GetListener();
        await using ISimpleNetworkConnection clientConnection = provider.GetClientConnection();

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        _ = clientConnection.ConnectAsync(default);

        // Act/Assert
        Assert.That(
            async () =>
            {
                await using ISimpleNetworkConnection _ = await acceptTask;
            },
            Throws.Nothing);
    }

    /// <summary>Verifies that calling read on a client connection with a disposed peer connection fails with <see
    /// cref="ConnectionLostException"/>.</summary>
    [Test]
    public async Task Client_connection_read_from_disposed_peer_connection_fails()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();
        await sut.ServerConnection.DisposeAsync();

        // Act/Assert
        Assert.That(async () => await sut.ClientConnection.ReadAsync(new byte[1], default),
            Throws.InstanceOf<ConnectionLostException>());
    }

    /// <summary>Verifies that pending write operation fails with <see cref="OperationCanceledException"/> once the
    /// cancellation token is canceled.</summary>
    [Test]
    public async Task Cancel_read()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();
        var buffer = new Memory<byte>(new byte[1]);
        using var canceled = new CancellationTokenSource();
        Task readTask = sut.ClientConnection.ReadAsync(buffer, canceled.Token).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(10));

        // Act
        canceled.Cancel();

        // Assert
        Assert.That(async () => await readTask, Throws.TypeOf<OperationCanceledException>());
    }

    /// <summary>Verifies that pending write operation fails with <see cref="OperationCanceledException"/> once the
    /// cancellation token is canceled.</summary>
    [Test]
    public async Task Cancel_write()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1024 * 1024] };
        using var canceled = new CancellationTokenSource();
        // Write completes as soon as the data is copied to the socket buffer, the test relies on the calls
        // not completing synchronously to be able to cancel them.
        Task writeTask;
        do
        {
            writeTask = sut.ClientConnection.WriteAsync(buffer, canceled.Token).AsTask();
            await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(100)), writeTask);
        }
        while (writeTask.IsCompleted);

        // Act
        canceled.Cancel();

        // Assert
        Assert.That(async () => await writeTask, Throws.TypeOf<OperationCanceledException>());
    }

    /// <summary>Verifies that we can write using server and client connections.</summary>
    [Test]
    public async Task Connection_write(
        [Values(1, 1024, 16 * 1024, 512 * 1024)] int size,
        [Values(true, false)] bool useServerConnection)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();
        byte[] writeBuffer = new byte[size];

        ISimpleNetworkConnection writeConnection = useServerConnection ? sut.ServerConnection : sut.ClientConnection;
        ISimpleNetworkConnection readConnection = useServerConnection ? sut.ClientConnection : sut.ServerConnection;

        // Act

        // The write is performed in the background. Otherwise, the transport flow control might cause it to block
        // until data is read.
        ValueTask writeTask = writeConnection.WriteAsync(new ReadOnlyMemory<byte>[] { writeBuffer }, default);

        // Assert
        Memory<byte> readBuffer = new byte[size];
        int offset = 0;
        while (offset < size)
        {
            offset += await readConnection.ReadAsync(readBuffer[offset..], default);
        }
        Assert.That(offset, Is.EqualTo(size));
        await writeTask;
    }

    /// <summary>Write data until the transport flow control start blocking, at this point we start
    /// a read task and ensure that this unblocks the pending write calls.</summary>
    [Test]
    public async Task Flow_control()
    {
        var payload = new List<ReadOnlyMemory<byte>>() { new byte[1024 * 1024] };
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();

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
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using IListener<ISimpleNetworkConnection> listener = provider.GetListener();

        // Act/Assert
        Assert.That(
            () => provider.CreateListener(listener.Endpoint),
            Throws.TypeOf<TransportException>());
    }

    [Test]
    public async Task Read_canceled()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using IListener<ISimpleNetworkConnection> listener = provider.GetListener();
        await using ISimpleNetworkConnection clientConnection = provider.GetClientConnection();
        var buffer = new Memory<byte>(new byte[1]);

        Assert.CatchAsync<OperationCanceledException>(
            async () => await clientConnection.ReadAsync(buffer, new CancellationToken(canceled: true)));
    }

    /// <summary>Verifies that a read operation ends with <see cref="OperationCanceledException"/> if the given
    /// cancellation token is canceled.</summary>
    [Test]
    public async Task Read_cancellation()
    {
        // Arrange
        using var canceled = new CancellationTokenSource();
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();
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
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using IListener<ISimpleNetworkConnection> listener = provider.GetListener();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();

        ISimpleNetworkConnection readFrom = readFromServer ? sut.ServerConnection : sut.ClientConnection;
        ISimpleNetworkConnection disposedPeer = readFromServer ? sut.ClientConnection : sut.ServerConnection;

        await disposedPeer.DisposeAsync();

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
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();
        ISimpleNetworkConnection disposedConnection = disposeServerConnection ? sut.ServerConnection : sut.ClientConnection;

        await disposedConnection.DisposeAsync();

        // Act/Assert
        Assert.That(
            async () => await disposedConnection.ReadAsync(new byte[1], default),
            Throws.TypeOf<ObjectDisposedException>());
    }

    /// <summary>Verifies that reading from the connection updates its last activity property.</summary>
    [Test]
    public async Task Read_updates_last_activity()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();
        await sut.ServerConnection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
        var delay = TimeSpan.FromMilliseconds(2);
        TimeSpan lastActivity = sut.ClientConnection.LastActivity;
        await Task.Delay(delay);
        var buffer = new Memory<byte>(new byte[1]);

        // Act
        await sut.ClientConnection.ReadAsync(buffer, default);

        // Assert
        Assert.That(
            sut.ClientConnection.LastActivity >= delay + lastActivity || sut.ClientConnection.LastActivity == TimeSpan.Zero,
            Is.True);
    }

    /// <summary>Verifies that pending write operation fails with <see cref="OperationCanceledException"/> once the
    /// cancellation token is canceled.</summary>
    [Test]
    public async Task Write_cancellation()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();
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
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();

        // Act
        await sut.ServerConnection.DisposeAsync();

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
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();
        ISimpleNetworkConnection disposedConnection =
            disposeServerConnection ? sut.ServerConnection : sut.ClientConnection;

        await disposedConnection.DisposeAsync();

        // Act/Assert
        Assert.That(
            async () => await disposedConnection.WriteAsync(
                new List<ReadOnlyMemory<byte>>() { new byte[1024] },
                default),
            Throws.TypeOf<ObjectDisposedException>());
    }

    /// <summary>Verifies that reading from the connection updates its last activity property.</summary>
    [Test]
    public async Task Write_updates_last_activity()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();
        var delay = TimeSpan.FromMilliseconds(10);
        TimeSpan lastActivity = sut.ClientConnection.LastActivity;
        await Task.Delay(delay);

        // Act
        await sut.ClientConnection.WriteAsync(new List<ReadOnlyMemory<byte>>() { new byte[1] }, default);

        // Assert
        Assert.That(
            sut.ClientConnection.LastActivity >= delay + lastActivity ||
            sut.ClientConnection.LastActivity == TimeSpan.Zero,
            Is.True);
    }

    /// <summary>Verifies that we can write using server and client connections.</summary>
    [Test]
    public async Task Write(
        [Values(1, 1024, 16 * 1024, 32 * 1024, 64 * 1024, 1024 * 1024)] int size,
        [Values(true, false)] bool useServerConnection)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using ClientServerSimpleTransportConnection sut = await provider.ConnectAndAcceptAsync();
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

    [Test]
    public async Task Write_canceled()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider();
        await using IListener<ISimpleNetworkConnection> listener = provider.GetListener();
        await using ISimpleNetworkConnection clientConnection = provider.GetClientConnection();
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1024] };

        Assert.CatchAsync<OperationCanceledException>(
            async () => await clientConnection.WriteAsync(buffer, new CancellationToken(canceled: true)));
    }

    /// <summary>Creates the service collection used for the simple transport conformance tests.</summary>
    protected abstract ServiceCollection CreateServiceCollection();
}

public class SimpleTransportServiceCollection : ServiceCollection
{
    public SimpleTransportServiceCollection()
    {
        this.AddScoped(provider =>
        {
            SslServerAuthenticationOptions? serverAuthenticationOptions =
                provider.GetService<SslServerAuthenticationOptions>();
            IServerTransport<ISimpleNetworkConnection>? serverTransport =
                provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();
            return serverTransport.Listen(
                provider.GetRequiredService<Endpoint>(),
                serverAuthenticationOptions,
                NullLogger.Instance);
        });

        this.AddScoped(provider =>
        {
            SslClientAuthenticationOptions? clientAuthenticationOptions =
                provider.GetService<SslClientAuthenticationOptions>();
            IListener<ISimpleNetworkConnection> listener = provider.GetListener();
            IClientTransport<ISimpleNetworkConnection> clientTransport =
                provider.GetRequiredService<IClientTransport<ISimpleNetworkConnection>>();
            return clientTransport.CreateConnection(
                listener.Endpoint,
                clientAuthenticationOptions,
                NullLogger.Instance);
        });
    }
}

public static class SimpleTransportServiceProviderExtensions
{
    public static IListener<ISimpleNetworkConnection> GetListener(this IServiceProvider provider) =>
        provider.GetRequiredService<IListener<ISimpleNetworkConnection>>();

    public static IListener<ISimpleNetworkConnection> CreateListener(this IServiceProvider provider, Endpoint endpoint)
    {
        SslServerAuthenticationOptions? serverAuthenticationOptions =
            provider.GetService<SslServerAuthenticationOptions>();
        IServerTransport<ISimpleNetworkConnection>? serverTransport =
            provider.GetRequiredService<IServerTransport<ISimpleNetworkConnection>>();
        return serverTransport.Listen(endpoint, serverAuthenticationOptions, NullLogger.Instance);
    }

    public static ISimpleNetworkConnection GetClientConnection(this IServiceProvider provider) =>
        provider.GetRequiredService<ISimpleNetworkConnection>();

    public static async Task<ClientServerSimpleTransportConnection> ConnectAndAcceptAsync(
        this IServiceProvider provider)
    {
        var listener = provider.GetRequiredService<IListener<ISimpleNetworkConnection>>();
        ISimpleNetworkConnection clientConnection = provider.GetClientConnection();
        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        Task<NetworkConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        Task<NetworkConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(default);
        await Task.WhenAll(clientConnectTask, serverConnectTask);
        return new ClientServerSimpleTransportConnection(clientConnection, serverConnection);
    }
}

public record struct ClientServerSimpleTransportConnection : IAsyncDisposable
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

    public async ValueTask DisposeAsync()
    {
        await ClientConnection.DisposeAsync();
        await ServerConnection.DisposeAsync();
    }
}
