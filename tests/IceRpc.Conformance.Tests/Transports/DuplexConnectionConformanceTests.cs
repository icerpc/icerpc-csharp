// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Net;
using System.Net.Security;

namespace IceRpc.Conformance.Tests;

/// <summary>Conformance tests for the duplex transports.</summary>
public abstract class DuplexConnectionConformanceTests
{
    /// <summary>Verifies that connect cancellation works if connect hangs.</summary>
    [Test]
    public async Task Connect_cancellation()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        IListener<IDuplexConnection> listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        var clientTransport = provider.GetRequiredService<IDuplexClientTransport>();

        // A duplex transport listener might use a backlog to accept client connections (e.g.: TCP). So we need to
        // create and establish client connections until a connection establishment blocks to test cancellation.
        using var cts = new CancellationTokenSource();
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
                connectTask = connection.ConnectAsync(cts.Token);
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
        cts.Cancel();

        // Assert
        Assert.That(async () => await connectTask, Throws.InstanceOf<OperationCanceledException>());
        clientConnection.Dispose();
    }

    [Test]
    public async Task Connect_succeeds_or_fails_with_connection_aborted_if_the_peer_disposes_the_connection_after_connect(
        [Values(true, false)] bool serverDispose)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var transport = provider.GetRequiredService<IDuplexClientTransport>().Name;

        // Act
        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        var clientConnection = provider.GetRequiredService<IDuplexConnection>();

        Task<(IDuplexConnection, EndPoint)> acceptTask = listener.AcceptAsync(default);
        Task<TransportConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        (IDuplexConnection serverConnection, EndPoint _) = await acceptTask;
        Task<TransportConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(default);

        await (serverDispose ? serverConnectTask : clientConnectTask);

        // Act
        if (serverDispose)
        {
            serverConnection.Dispose();
        }
        else
        {
            clientConnection.Dispose();
        }

        // Assert
        IceRpcException? exception = null;
        try
        {
            await (serverDispose ? clientConnectTask : serverConnectTask);
        }
        catch (IceRpcException ex)
        {
            exception = ex;
        }

        // On some platforms (Linux and macOS), the connection establishment can fail on the client side.
        if (serverDispose)
        {
            Assert.That(
                exception?.IceRpcError,
                Is.Null.Or.EqualTo(IceRpcError.ConnectionAborted).Or.EqualTo(IceRpcError.IceRpcError));
        }
        else
        {
            Assert.That(exception?.IceRpcError, Is.Null.Or.EqualTo(IceRpcError.ConnectionAborted));
        }
    }

    [Test]
    public async Task Create_client_connection_with_unknown_server_address_parameter_fails_with_format_exception()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientTransport = provider.GetRequiredService<IDuplexClientTransport>();

        var serverAddress = new ServerAddress(new Uri("icerpc://foo?unknown-parameter=foo"));

        // Act/Asserts
        Assert.Throws<ArgumentException>(
            () => clientTransport.CreateConnection(serverAddress, new DuplexConnectionOptions(), null));
    }

    [Test]
    public async Task Create_server_connection_with_unknown_server_address_parameter_fails_with_format_exception()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var serverTransport = provider.GetRequiredService<IDuplexServerTransport>();

        var serverAddress = new ServerAddress(new Uri("icerpc://foo?unknown-parameter=foo"));

        // Act/Asserts
        Assert.Throws<ArgumentException>(
            () => serverTransport.Listen(serverAddress, new DuplexConnectionOptions(), null));
    }

    /// <summary>Write data until the transport flow control starts blocking, at this point we start a read task and
    /// ensure that this unblocks the pending write calls.</summary>
    [Test]
    public async Task Flow_control()
    {
        var payload = new List<ReadOnlyMemory<byte>>() { new byte[1024 * 1024] };
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());

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
        Assert.That(async () => await writeTask, Throws.Nothing);
        Assert.That(async () => await readTask, Throws.Nothing);

        static async Task ReadAsync(IDuplexConnection connection, int size)
        {
            var buffer = new byte[1024];
            while (size > 0)
            {
                size -= await connection.ReadAsync(buffer, default);
            }
        }
    }

    [Test]
    public async Task Read_canceled()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());
        var buffer = new Memory<byte>(new byte[1]);

        Assert.That(
            async () => await sut.ClientConnection.ReadAsync(buffer, new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that a read operation ends with <see cref="OperationCanceledException" /> if the given
    /// cancellation token is canceled.</summary>
    [Test]
    public async Task Read_cancellation()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());
        ValueTask<int> readTask = sut.ClientConnection.ReadAsync(new byte[1], cts.Token);

        // Act
        cts.Cancel();

        // Assert
        Assert.That(async () => await readTask, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that calling read on a connection fails with
    /// <see cref="IceRpcError.ConnectionAborted" /> if the peer connection is disposed.</summary>
    [Test]
    public async Task Read_from_disposed_peer_connection_fails_with_connection_aborted(
        [Values(true, false)] bool readFromServer)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());

        IDuplexConnection readFrom = readFromServer ? sut.ServerConnection : sut.ClientConnection;
        IDuplexConnection disposedPeer = readFromServer ? sut.ClientConnection : sut.ServerConnection;

        disposedPeer.Dispose();

        // Act/Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await readFrom.ReadAsync(new byte[1], default));
        Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.ConnectionAborted));
    }

    [Test]
    public async Task Read_returns_zero_after_shutdown()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());

        // Act
        await sut.ServerConnection.ShutdownAsync(CancellationToken.None);
        int clientRead = await sut.ClientConnection.ReadAsync(new byte[1], CancellationToken.None);

        await sut.ClientConnection.ShutdownAsync(CancellationToken.None);
        int serverRead = await sut.ServerConnection.ReadAsync(new byte[1], CancellationToken.None);

        // Assert
        Assert.That(clientRead, Is.EqualTo(0));
        Assert.That(serverRead, Is.EqualTo(0));
    }

    [Test]
    public async Task Shutdown_connection()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .BuildServiceProvider(validateScopes: true);
        IDuplexConnection clientConnection = provider.GetRequiredService<IDuplexConnection>();
        IListener<IDuplexConnection> listener = provider.GetRequiredService<IListener<IDuplexConnection>>();

        var clientConnectTask = clientConnection.ConnectAsync(CancellationToken.None);
        (IDuplexConnection serverConnection, _) = await listener.AcceptAsync(CancellationToken.None);
        await serverConnection.ConnectAsync(CancellationToken.None);
        await clientConnectTask;

        // Act/Assert
        Assert.That(
            async () => await clientConnection.ShutdownAsync(CancellationToken.None),
            Throws.Nothing);

        Assert.That(
            async () => await serverConnection.ShutdownAsync(CancellationToken.None),
            Throws.Nothing);
    }

    [Test]
    public async Task Shutdown_connection_on_both_sides()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .BuildServiceProvider(validateScopes: true);
        IDuplexConnection clientConnection = provider.GetRequiredService<IDuplexConnection>();
        IListener<IDuplexConnection> listener = provider.GetRequiredService<IListener<IDuplexConnection>>();

        var clientConnectTask = clientConnection.ConnectAsync(CancellationToken.None);
        (IDuplexConnection serverConnection, _) = await listener.AcceptAsync(CancellationToken.None);
        await serverConnection.ConnectAsync(CancellationToken.None);
        await clientConnectTask;

        // Act
        var clientShutdownTask = clientConnection.ShutdownAsync(CancellationToken.None);
        var serverShutdownTask = serverConnection.ShutdownAsync(CancellationToken.None);

        // Assert
        Assert.That(async () => await clientShutdownTask, Throws.Nothing);
        Assert.That(async () => await serverShutdownTask, Throws.Nothing);
    }

    /// <summary>Verifies that we can write and read using the duplex connection.</summary>
    [Test]
    public async Task Write_and_read_buffers(
        [Values(
            new int[] { 1 },
            new int[] { 1024 },
            new int[] { 32 * 1024 },
            new int[] { 1024 * 1024 },
            new int[] { 16, 32, 64, 128 },
            new int[] { 3, 9, 15, 512 * 1024},
            new int[] { 3, 512 * 1024})] int[] sizes)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());

        int size = sizes.Sum();
        ReadOnlyMemory<byte>[] buffers =
            sizes.Select(
                n => (ReadOnlyMemory<byte>)Enumerable.Range(0, n).Select(i => (byte)(i % 255)).ToArray())
            .ToArray();

        // Act
        ValueTask writeTask = sut.ClientConnection.WriteAsync(buffers, default);
        Memory<byte> readBuffer = new byte[size];
        int offset = 0;
        while (offset < size)
        {
            offset += await sut.ServerConnection.ReadAsync(readBuffer[offset..], default);
        }
        await writeTask;

        // Assert
        Assert.That(offset, Is.EqualTo(size));
        offset = 0;
        for (int i = 0; i < sizes.Length; ++i)
        {
            size = sizes[i];
            Assert.That(readBuffer.Span.Slice(offset, size).SequenceEqual(buffers[i].Span), Is.True);
            offset += size;
        }
    }

    [Test]
    public async Task Write_canceled()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1] };

        Assert.That(
            async () => await sut.ClientConnection.WriteAsync(buffer, new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that pending write operation fails with <see cref="OperationCanceledException" /> once the
    /// cancellation token is canceled.</summary>
    [Test]
    public async Task Write_cancellation()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1024 * 1024] };
        using var cts = new CancellationTokenSource();

        // Write data until flow control blocks the sending. Canceling the blocked write, should throw
        // OperationCanceledException.
        Task writeTask;
        while (true)
        {
            writeTask = sut.ClientConnection.WriteAsync(buffer, cts.Token).AsTask();
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
        cts.Cancel();

        // Assert
        Assert.That(async () => await writeTask, Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task Write_fails_after_shutdown()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());
        await sut.ServerConnection.ShutdownAsync(CancellationToken.None);

        // Act/Assert
        Assert.That(
            async () => await sut.ServerConnection.WriteAsync(new List<ReadOnlyMemory<byte>> { new byte[1] },
                CancellationToken.None),
                Throws.Exception);
    }

    /// <summary>Verifies that calling write fails with <see cref="IceRpcError.ConnectionAborted" />
    /// when the peer connection is disposed.</summary>
    [Test]
    public async Task Write_to_disposed_peer_connection_fails_with_connection_aborted()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());

        // Act
        sut.ServerConnection.Dispose();

        // Assert
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1] };
        IceRpcException exception;
        try
        {
            // It can take few writes to detect the peer's connection closure.
            while (true)
            {
                await sut.ClientConnection.WriteAsync(buffer, default);
                await Task.Delay(50);
            }
        }
        catch (IceRpcException ex)
        {
            exception = ex;
        }

        Assert.That(exception.IceRpcError, Is.EqualTo(IceRpcError.ConnectionAborted));
    }

    /// <summary>Creates the service collection used for the duplex transport conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();

    private static async Task<ClientServerDuplexConnection> ConnectAndAcceptAsync(
        IListener<IDuplexConnection> listener,
        IDuplexConnection clientConnection)
    {
        Task<(IDuplexConnection, EndPoint)> acceptTask = listener.AcceptAsync(default);
        Task<TransportConnectionInformation> clientConnectTask = clientConnection.ConnectAsync(default);
        (IDuplexConnection serverConnection, EndPoint _) = await acceptTask;
        Task<TransportConnectionInformation> serverConnectTask = serverConnection.ConnectAsync(default);
        await Task.WhenAll(clientConnectTask, serverConnectTask);
        return new ClientServerDuplexConnection(clientConnection, serverConnection);
    }
}

public record struct ClientServerDuplexConnection : IDisposable
{
    public IDuplexConnection ClientConnection { get; }
    public IDuplexConnection ServerConnection { get; }

    public ClientServerDuplexConnection(
        IDuplexConnection clientConnection,
        IDuplexConnection serverConnection)
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
