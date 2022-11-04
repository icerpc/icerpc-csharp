// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Net;
using System.Net.Security;

namespace IceRpc.Conformance.Tests;

/// <summary>Conformance tests for the duplex transports.</summary>
public abstract class DuplexTransportConformanceTests
{
    [OneTimeSetUp]
    public void OneTimeSetup() => AssertTraceListener.Setup();

    /// <summary>Verifies that the transport can accept connections.</summary>
    [Test]
    public async Task Accept_transport_connection()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        var clientConnection = provider.GetRequiredService<IDuplexConnection>();

        Task<(IDuplexConnection Connection, EndPoint RemoteNetworkAddress)> acceptTask = listener.AcceptAsync(default);
        _ = clientConnection.ConnectAsync(default);

        // Act/Assert
        Assert.That(
            async () =>
            {
                using IDuplexConnection _ = (await acceptTask).Connection;
            },
            Throws.Nothing);
    }

    [Test]
    public async Task Call_accept_with_canceled_cancelation_token_fails_with_operation_canceled()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();

        // Act / Assert
        Assert.That(
            async () => await listener.AcceptAsync(new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());
    }

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

    [Test]
    public async Task Call_accept_then_cancel_the_cancellation_source_and_dispose_the_listener_fails_with_operation_canceled_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        IListener<IDuplexConnection> listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        using var cancelationSource = new CancellationTokenSource();

        var acceptTask = listener.AcceptAsync(cancelationSource.Token);

        // Act
        cancelationSource.Cancel();
        await listener.DisposeAsync();

        // Assert
        Assert.That(async () => await acceptTask, Throws.TypeOf<OperationCanceledException>());
    }

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

    /// <summary>Verifies that connect fails if the listener is disposed.</summary>
    [Test]
    public async Task Connect_fails_if_listener_is_disposed()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
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
        Assert.That(async () => await connectTask, Throws.InstanceOf<TransportException>());
        clientConnection.Dispose();
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
    public async Task Listen_twice_on_the_same_address_fails_with_a_transport_exception()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        var serverTransport = provider.GetRequiredService<IDuplexServerTransport>();

        // Act/Assert
        TransportException? exception = Assert.Throws<TransportException>(
            () => serverTransport.Listen(listener.ServerAddress, new DuplexConnectionOptions(), null));
        Assert.That(exception!.ErrorCode, Is.EqualTo(TransportErrorCode.AddressInUse));
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

        Assert.CatchAsync<OperationCanceledException>(
            async () => await sut.ClientConnection.ReadAsync(buffer, new CancellationToken(canceled: true)));
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

    /// <summary>Verifies that calling read on a connection fails with <see cref="TransportErrorCode.ConnectionReset" />
    /// if the peer connection is disposed.</summary>
    [Test]
    public async Task Read_from_disposed_peer_connection_fails_with_transport_reset_error(
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
        TransportException? exception = Assert.ThrowsAsync<TransportException>(
            async () => await readFrom.ReadAsync(new byte[1], default));
        Assert.That(exception!.ErrorCode, Is.EqualTo(TransportErrorCode.ConnectionReset));
    }

    /// <summary>Verifies that calling read on a disposed connection fails with <see cref="ObjectDisposedException" />.
    /// </summary>
    [Test]
    public async Task Read_from_disposed_connection_fails([Values(true, false)] bool disposeServerConnection)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());
        IDuplexConnection disposedConnection = disposeServerConnection ? sut.ServerConnection : sut.ClientConnection;

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
    public async Task Create_client_connection_with_unknown_server_address_parameter_fails_with_format_exception()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientTransport = provider.GetRequiredService<IDuplexClientTransport>();

        var serverAddress = new ServerAddress(new Uri("icerpc://foo?unknown-parameter=foo"));

        // Act/Asserts
        Assert.Throws<FormatException>(
            () => clientTransport.CreateConnection(serverAddress, new DuplexConnectionOptions(), null));
    }

    [Test]
    public async Task Create_server_connection_with_unknown_server_address_parameter_fails_with_format_exception()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var serverTransport = provider.GetRequiredService<IDuplexServerTransport>();

        var serverAddress = new ServerAddress(new Uri("icerpc://foo?unknown-parameter=foo"));

        // Act/Asserts
        Assert.Throws<FormatException>(() => serverTransport.Listen(serverAddress, new DuplexConnectionOptions(), null));
    }

    [Test]
    public async Task Connection_server_address_transport_property_is_set()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var transport = provider.GetRequiredService<IDuplexClientTransport>().Name;

        // Act
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());

        // Assert
        Assert.That(sut.ClientConnection.ServerAddress.Transport, Is.EqualTo(transport));
        Assert.That(sut.ServerConnection.ServerAddress.Transport, Is.EqualTo(transport));
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

    [Test]
    public async Task Shutdown_client_connection_before_connect_fails_with_transport_connection_shutdown_error()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IDuplexConnection>();

        // Act
        await clientConnection.ShutdownAsync(default);

        // Assert
        TransportException? exception = Assert.ThrowsAsync<TransportException>(
            async () => await clientConnection.ConnectAsync(default));
        Assert.That(exception!.ErrorCode, Is.EqualTo(TransportErrorCode.ConnectionShutdown));
    }

    [Test]
    public async Task Shutdown_by_peer_before_connect_fails_with_transport_reset_error()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IDuplexConnection>>();
        var clientConnection = provider.GetRequiredService<IDuplexConnection>();

        Task acceptTask = AcceptAndShutdownAsync();

        // Act
        Exception? exception = null;
        try
        {
            await clientConnection.ConnectAsync(default);

            // Connect might succeed if ConnectAsync doesn't require additional data exchange after connecting. It's the
            // case for raw TCP which only connects the socket.
            await clientConnection.ShutdownAsync(default);
        }
        catch (Exception ex)
        {
            exception = ex;
        }

        // Assert
        Assert.That(exception, Is.Null.Or.InstanceOf<TransportException>());
        if (exception is TransportException transportException)
        {
            Assert.That(transportException!.ErrorCode, Is.EqualTo(TransportErrorCode.ConnectionReset));
        }

        async Task AcceptAndShutdownAsync()
        {
            (IDuplexConnection connection, EndPoint remoteNetworkAddress) = await listener.AcceptAsync(default);
            await connection.ShutdownAsync(default);
            connection.Dispose();
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

        Assert.CatchAsync<OperationCanceledException>(
            async () => await sut.ClientConnection.WriteAsync(buffer, new CancellationToken(canceled: true)));
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

    /// <summary>Verifies that calling write fails with <see cref="TransportErrorCode.ConnectionReset" /> when the peer
    /// connection is disposed.</summary>
    [Test]
    public async Task Write_to_disposed_peer_connection_fails_with_transport_reset_error()
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
        TransportException exception;
        try
        {
            // It can take few writes to detect the peer's connection closure.
            while (true)
            {
                await sut.ClientConnection.WriteAsync(buffer, default);
                await Task.Delay(50);
            }
        }
        catch (TransportException ex)
        {
            exception = ex;
        }

        Assert.That(exception.ErrorCode, Is.EqualTo(TransportErrorCode.ConnectionReset));
    }

    /// <summary>Verifies that calling read on a disposed connection fails with <see cref="ObjectDisposedException" />.
    /// </summary>
    [Test]
    public async Task Write_to_disposed_connection_fails([Values(true, false)] bool disposeServerConnection)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());
        IDuplexConnection disposedConnection =
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
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());
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
        using ClientServerDuplexConnection sut = await ConnectAndAcceptAsync(
            provider.GetRequiredService<IListener<IDuplexConnection>>(),
            provider.GetRequiredService<IDuplexConnection>());
        byte[] writeBuffer = Enumerable.Range(0, size).Select(i => (byte)(i % 255)).ToArray();

        IDuplexConnection writeConnection = useServerConnection ? sut.ServerConnection : sut.ClientConnection;
        IDuplexConnection readConnection = useServerConnection ? sut.ClientConnection : sut.ServerConnection;

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
        Assert.That(offset, Is.EqualTo(size));
        Assert.That(readBuffer.Span.SequenceEqual(writeBuffer), Is.True);

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
