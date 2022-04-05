// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

/// <summary>Conformance tests for the simple transports.</summary>
[Timeout(30000)]
[Parallelizable(ParallelScope.All)]
public abstract class SimpleTransportConformanceTests
{
    /// <summary>Verifies that the transport can accept connections.</summary>
    [Test]
    public async Task Accept_network_connection()
    {
        // Arrange
        ISimpleTransportTestFixture testFixture = CreateSimpleTransportTestFixture();
        await using IListener<ISimpleNetworkConnection> listener = testFixture.CreateListener();
        await using ISimpleNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);

        // Act/Assert
        Assert.That(
            async () =>
            {
                await using ISimpleNetworkConnection _ = await acceptTask;
            },
            Throws.Nothing);
    }

    /// <summary>Verifies that calling read on a connection fails with <see cref="ConnectionLostException"/> if the
    /// peer connection is disposed.</summary>
    [Test]
    public async Task Read_from_disposed_peer_connection_fails_with_connection_lost_exception(
        [Values(true, false)] bool readFromServer)
    {
        // Arrange
        ISimpleTransportTestFixture testFixture = CreateSimpleTransportTestFixture();
        await using IListener<ISimpleNetworkConnection> listener = testFixture.CreateListener();
        await using ISimpleNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;

        ISimpleNetworkConnection readFrom = readFromServer ? serverConnection : clientConnection;
        ISimpleNetworkConnection disposedPeer = readFromServer ? clientConnection : serverConnection;

        await disposedPeer.DisposeAsync();

        // Act/Assert
        Assert.That(
            async () => await readFrom.ReadAsync(new byte[1], default),
            Throws.TypeOf<ConnectionLostException>());
    }

    /// <summary>Verifies that we can write using server and client connections.</summary>
    [Test]
    public async Task Connection_write(
        [Values(1, 1024, 16 * 1024, 32 * 1024, 64 * 1024, 1024 * 1024)] int size,
        [Values(true, false)] bool useServerConnection)
    {
        // Arrange
        ISimpleTransportTestFixture testFixture = CreateSimpleTransportTestFixture();
        await using IListener<ISimpleNetworkConnection> listener = testFixture.CreateListener();
        await using ISimpleNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        byte[] writeBuffer = Enumerable.Range(0, size).Select(i => (byte)(i % 255)).ToArray();

        ISimpleNetworkConnection writeConnection = useServerConnection ? serverConnection : clientConnection;
        ISimpleNetworkConnection readConnection = useServerConnection ? clientConnection : serverConnection;

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
        Assert.That(readBuffer.ToArray(), Is.EqualTo(writeBuffer));
    }

    [Test]
    public void Listen_twice_on_the_same_address_fails_with_a_transport_exception()
    {
        // Arrange
        ISimpleTransportTestFixture testFixture = CreateSimpleTransportTestFixture();
        IListener<ISimpleNetworkConnection> listener = testFixture.CreateListener();

        // Act/Assert
        Assert.That(
            () => testFixture.CreateListener(listener.Endpoint),
            Throws.TypeOf<TransportException>());
    }

    /// <summary>Verifies that calling read on a disposed connection fails with <see cref="ObjectDisposedException"/>.
    /// </summary>
    [Test]
    public async Task Read_from_disposed_connection_fails([Values(true, false)] bool disposeServerConnection)
    {
        // Arrange
        ISimpleTransportTestFixture testFixture = CreateSimpleTransportTestFixture();
        await using IListener<ISimpleNetworkConnection> listener = testFixture.CreateListener();
        await using ISimpleNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        ISimpleNetworkConnection disposedConnection = disposeServerConnection ? serverConnection : clientConnection;

        await disposedConnection.DisposeAsync();

        // Act/Assert
        Assert.That(
            async () => await disposedConnection.ReadAsync(new byte[1], default),
            Throws.TypeOf<ObjectDisposedException>());
    }

    /// <summary>Verifies that calling read on a disposed connection fails with <see cref="ObjectDisposedException"/>.
    /// </summary>
    [Test]
    public async Task Write_to_disposed_connection_fails([Values(true, false)] bool disposeServerConnection)
    {
        // Arrange
        ISimpleTransportTestFixture testFixture = CreateSimpleTransportTestFixture();
        await using IListener<ISimpleNetworkConnection> listener = testFixture.CreateListener();
        await using ISimpleNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        ISimpleNetworkConnection disposedConnection = disposeServerConnection ? serverConnection : clientConnection;

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
    public async Task Read_updates_last_activity()
    {
        // Arrange
        ISimpleTransportTestFixture testFixture = CreateSimpleTransportTestFixture();
        await using IListener<ISimpleNetworkConnection> listener = testFixture.CreateListener();
        await using ISimpleNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        await serverConnection.WriteAsync(new ReadOnlyMemory<byte>[] { new byte[1] }, default);
        var delay = TimeSpan.FromMilliseconds(2);
        TimeSpan lastActivity = clientConnection.LastActivity;
        await Task.Delay(delay);
        var buffer = new Memory<byte>(new byte[1]);

        // Act
        await clientConnection.ReadAsync(buffer, default);

        // Assert
        Assert.That(
            clientConnection.LastActivity >= delay + lastActivity || clientConnection.LastActivity == TimeSpan.Zero,
            Is.True);
    }

    /// <summary>Verifies that pending write operation fails with <see cref="OperationCanceledException"/> once the
    /// cancellation token is canceled.</summary>
    [Test]
    public async Task Cancel_read()
    {
        // Arrange
        ISimpleTransportTestFixture testFixture = CreateSimpleTransportTestFixture();
        await using IListener<ISimpleNetworkConnection> listener = testFixture.CreateListener();
        await using ISimpleNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        var buffer = new Memory<byte>(new byte[1]);
        using var canceled = new CancellationTokenSource();
        // Write completes as soon as the data is copied to the socket buffer, the test relies on the calls
        // not completing synchronously to be able to cancel them.
        Task readTask = clientConnection.ReadAsync(buffer, canceled.Token).AsTask();
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
        ISimpleTransportTestFixture testFixture = CreateSimpleTransportTestFixture();
        await using IListener<ISimpleNetworkConnection> listener = testFixture.CreateListener();
        await using ISimpleNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);

        Task<ISimpleNetworkConnection> acceptTask = listener.AcceptAsync();
        await clientConnection.ConnectAsync(default);
        ISimpleNetworkConnection serverConnection = await acceptTask;
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1024 * 1024] };
        using var canceled = new CancellationTokenSource();
        // Write completes as soon as the data is copied to the socket buffer, the test relies on the calls
        // not completing synchronously to be able to cancel them.
        Task writeTask;
        do
        {
            writeTask = clientConnection.WriteAsync(buffer, canceled.Token).AsTask();
            await Task.WhenAny(Task.Delay(TimeSpan.FromMilliseconds(100)), writeTask);
        }
        while (writeTask.IsCompleted);

        // Act
        canceled.Cancel();

        // Assert
        Assert.That(async () => await writeTask, Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public async Task Write_canceled()
    {
        ISimpleTransportTestFixture testFixture = CreateSimpleTransportTestFixture();
        await using IListener<ISimpleNetworkConnection> listener = testFixture.CreateListener();
        await using ISimpleNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);
        var buffer = new List<ReadOnlyMemory<byte>>() { new byte[1024] };

        Assert.CatchAsync<OperationCanceledException>(
            async () => await clientConnection.WriteAsync(buffer, new CancellationToken(canceled: true)));
    }

    [Test]
    public async Task Read_canceled()
    {
        ISimpleTransportTestFixture testFixture = CreateSimpleTransportTestFixture();
        await using IListener<ISimpleNetworkConnection> listener = testFixture.CreateListener();
        await using ISimpleNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);
        var buffer = new Memory<byte>(new byte[1]);

        Assert.CatchAsync<OperationCanceledException>(
            async () => await clientConnection.ReadAsync(buffer, new CancellationToken(canceled: true)));
    }

    /// <summary>Creates the test fixture that provides the multiplexed transport to test with.</summary>
    public abstract ISimpleTransportTestFixture CreateSimpleTransportTestFixture();
}

public interface ISimpleTransportTestFixture
{
    /// <summary>Creates a listener using the underlying multiplexed server transport.</summary>
    /// <param name="endpoint">The listener endpoint</param>
    /// <returns>The listener.</returns>
    IListener<ISimpleNetworkConnection> CreateListener(Endpoint? endpoint = null);

    /// <summary>Creates a connection using the underlying multiplexed client transport.</summary>
    /// <param name="endpoint">The connection endpoint.</param>
    /// <returns>The connection.</returns>
    ISimpleNetworkConnection CreateConnection(Endpoint endpoint);
}

public class TcpTransportTestFixture : ISimpleTransportTestFixture
{
    private readonly TcpServerTransport _tcpServerTransport = new();
    private readonly TcpClientTransport _tcpClientTransport = new();

    public IListener<ISimpleNetworkConnection> CreateListener(Endpoint? endpoint = null) =>
        _tcpServerTransport.Listen(
            endpoint ?? Endpoint.FromString("icerpc://127.0.0.1:0/"),
            null,
            NullLogger.Instance);

    public ISimpleNetworkConnection CreateConnection(Endpoint endpoint) =>
        _tcpClientTransport.CreateConnection(endpoint, null, NullLogger.Instance);
}

public class ColocTransportTestFixture : ISimpleTransportTestFixture
{
    private readonly ColocTransport _transport = new();
    public IListener<ISimpleNetworkConnection> CreateListener(Endpoint? endpoint = null) =>
        _transport.ServerTransport.Listen(
            endpoint ?? Endpoint.FromString($"icerpc://{Guid.NewGuid()}/"),
            null,
            NullLogger.Instance);

    public ISimpleNetworkConnection CreateConnection(Endpoint endpoint) =>
        _transport.ClientTransport.CreateConnection(endpoint, null, NullLogger.Instance);
}

/// <summary>Conformance tests for the tcp simple transport.</summary>
[Timeout(30000)]
[Parallelizable(ParallelScope.All)]
public class TcpTransportConformanceTests : SimpleTransportConformanceTests
{
    public override ISimpleTransportTestFixture CreateSimpleTransportTestFixture() =>
        new TcpTransportTestFixture();
}

/// <summary>Conformance tests for the coloc simple transport.</summary>
public class ColocTransportConformanceTests : SimpleTransportConformanceTests
{
    public override ISimpleTransportTestFixture CreateSimpleTransportTestFixture() =>
        new ColocTransportTestFixture();
}