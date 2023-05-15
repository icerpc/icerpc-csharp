// Copyright (c) ZeroC, Inc.

using IceRpc.Transports;
using IceRpc.Transports.Coloc;
using IceRpc.Transports.Coloc.Internal;
using NUnit.Framework;
using System.Buffers;

namespace IceRpc.Tests.Transports.Coloc;

[Parallelizable(scope: ParallelScope.All)]
public class ColocTransportTests
{
    [Test]
    public async Task Coloc_transport_connection_information()
    {
        var colocTransport = new ColocTransport();
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);
        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        Task<TransportConnectionInformation> connectTask = clientConnection.ConnectAsync(default);
        using IDuplexConnection _ = (await listener.AcceptAsync(default)).Connection;

        // Act
        TransportConnectionInformation transportConnectionInformation = await connectTask;

        // Assert
        Assert.That(transportConnectionInformation.LocalNetworkAddress, Is.TypeOf<ColocEndPoint>());
        var localNetworkAddress = (ColocEndPoint?)transportConnectionInformation.LocalNetworkAddress;
        Assert.That(localNetworkAddress?.ToString(), Is.EqualTo(listener.ServerAddress.ToString()));
        Assert.That(transportConnectionInformation.RemoteNetworkAddress, Is.TypeOf<ColocEndPoint>());

        var remoteNetworkAddress = (ColocEndPoint?)transportConnectionInformation.RemoteNetworkAddress;

        Assert.That(remoteNetworkAddress?.ToString(), Is.EqualTo(listener.ServerAddress.ToString()));
        Assert.That(transportConnectionInformation.RemoteCertificate, Is.Null);
    }

    [TestCase(1)]
    [TestCase(10)]
    public async Task Coloc_transport_listener_backlog(int listenBacklog)
    {
        var colocTransport = new ColocTransport(new ColocTransportOptions { ListenBacklog = listenBacklog });
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        // Fill the pending connection queue.
        var connections = new List<(IDuplexConnection, Task)>();
        for (int i = 0; i < listenBacklog; ++i)
        {
            IDuplexConnection connection = colocTransport.ClientTransport.CreateConnection(
                serverAddress,
                new DuplexConnectionOptions(),
                null);
            connections.Add((connection, connection.ConnectAsync(default)));
        }

        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        // Act/Assert
        Assert.That(() => clientConnection.ConnectAsync(default), Throws.InstanceOf<IceRpcException>());
        await listener.DisposeAsync();
        foreach ((IDuplexConnection connection, Task connectTask) in connections)
        {
            Assert.That(() => connectTask, Throws.InstanceOf<IceRpcException>());
            connection.Dispose();
        }
    }

    [Test]
    public async Task Connect_called_twice_throws_invalid_operation_exception()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);
        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        var serverConnectionTask = listener.AcceptAsync(default);
        await clientConnection.ConnectAsync(default);
        using IDuplexConnection serverConnection = (await serverConnectionTask).Connection;

        // Assert
        Assert.That(async () => await clientConnection.ConnectAsync(default), Throws.InvalidOperationException);
    }

    /// <summary>Verifies that calling read on a disposed connection fails with <see cref="ObjectDisposedException" />.
    /// </summary>
    [Test]
    public async Task Read_from_disposed_connection_fails()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);
        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        clientConnection.Dispose();

        // Act/Assert
        Assert.That(
            async () => await clientConnection.ReadAsync(new byte[1], default),
            Throws.TypeOf<ObjectDisposedException>());
    }

    [Test]
    public async Task Read_before_connect_throws_invalid_operation_exception()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);
        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        // Assert
        Assert.That(
            async () => await clientConnection.ReadAsync(new byte[1], default),
            Throws.InvalidOperationException);
    }

    [Test]
    public async Task Read_while_read_is_in_progress_throws_invalid_operation_exception()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);
        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        var serverConnectionTask = listener.AcceptAsync(default);
        await clientConnection.ConnectAsync(default);
        using IDuplexConnection serverConnection = (await serverConnectionTask).Connection;

        byte[] buffer = new byte[1];
        var writeBuffer = new ReadOnlySequence<byte>(new byte[1]);

        // Act
        ValueTask<int> readTask = clientConnection.ReadAsync(buffer, default);

        // Assert
        Assert.That(async () => await clientConnection.ReadAsync(buffer, default), Throws.InvalidOperationException);
        Assert.That(async () => await serverConnection.WriteAsync(writeBuffer, default), Throws.Nothing);
        Assert.That(async () => await readTask, Is.EqualTo(1));
    }

    [Test]
    public async Task Shutdown_writes_before_connect_throws_invalid_operation_exception()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);
        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        // Assert
        Assert.That(async () => await clientConnection.ShutdownWriteAsync(default), Throws.InvalidOperationException);
    }

    [Test]
    public async Task Write_while_write_is_in_progress_throws_invalid_operation_exception()
    {
        var colocTransport = new ColocTransport(
            new ColocTransportOptions
            {
                PauseWriterThreshold = 2048,
                ResumeWriterThreshold = 1024
            });
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);
        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        var serverConnectionTask = listener.AcceptAsync(default);
        await clientConnection.ConnectAsync(default);
        using IDuplexConnection serverConnection = (await serverConnectionTask).Connection;
        var buffer = new ReadOnlySequence<byte>(new byte[4096]);

        // Act
        ValueTask writeTask = clientConnection.WriteAsync(buffer, default);

        // Assert
        Assert.That(() => clientConnection.WriteAsync(buffer, default), Throws.InstanceOf<InvalidOperationException>());
        Assert.That(async () => await serverConnection.ReadAsync(new byte[4096], default), Is.EqualTo(4096));
        Assert.That(async () => await writeTask, Throws.Nothing);
    }

    [Test]
    public async Task Shutdown_write_while_write_is_in_progress_throws_invalid_operation_exception()
    {
        var colocTransport = new ColocTransport(
            new ColocTransportOptions
            {
                PauseWriterThreshold = 2048,
                ResumeWriterThreshold = 1024
            });
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);
        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        var serverConnectionTask = listener.AcceptAsync(default);
        await clientConnection.ConnectAsync(default);
        using IDuplexConnection serverConnection = (await serverConnectionTask).Connection;
        var buffer = new ReadOnlySequence<byte>(new byte[4096]);

        // Act
        ValueTask writeTask = clientConnection.WriteAsync(buffer, default);

        // Assert
        Assert.That(() => clientConnection.ShutdownWriteAsync(default), Throws.InstanceOf<InvalidOperationException>());
        Assert.That(async () => await serverConnection.ReadAsync(new byte[4096], default), Is.EqualTo(4096));
        Assert.That(async () => await writeTask, Throws.Nothing);
    }

    [Test]
    public async Task Write_after_shutdown_write_throws_invalid_operation_exception()
    {
        var colocTransport = new ColocTransport(
            new ColocTransportOptions
            {
                PauseWriterThreshold = 2048,
                ResumeWriterThreshold = 1024
            });
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);
        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        var serverConnectionTask = listener.AcceptAsync(default);
        await clientConnection.ConnectAsync(default);
        using IDuplexConnection serverConnection = (await serverConnectionTask).Connection;
        var buffer = new ReadOnlySequence<byte>(new byte[1]);

        // Act
        await clientConnection.ShutdownWriteAsync(default);

        // Assert
        Assert.That(() => clientConnection.WriteAsync(buffer, default), Throws.InstanceOf<InvalidOperationException>());
    }

    [Test]
    public async Task Write_before_connect_throws_invalid_operation_exception()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);
        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        // Assert
        Assert.That(
            async () => await clientConnection.WriteAsync(new ReadOnlySequence<byte>(new byte[1]), default),
            Throws.InvalidOperationException);
    }

    /// <summary>Verifies that calling write on a disposed connection fails with <see cref="ObjectDisposedException" />.
    /// </summary>
    [Test]
    public async Task Write_to_disposed_connection_fails()
    {
        // Arrange
        var colocTransport = new ColocTransport();
        var serverAddress = new ServerAddress(new Uri($"icerpc://{Guid.NewGuid()}"));
        await using IListener<IDuplexConnection> listener = colocTransport.ServerTransport.Listen(
            serverAddress,
            new DuplexConnectionOptions(),
            null);
        using IDuplexConnection clientConnection = colocTransport.ClientTransport.CreateConnection(
            serverAddress,
            new DuplexConnectionOptions(),
            null);

        clientConnection.Dispose();

        // Act/Assert
        Assert.That(
            async () => await clientConnection.WriteAsync(new ReadOnlySequence<byte>(new byte[1]), default),
            Throws.TypeOf<ObjectDisposedException>());
    }
}
