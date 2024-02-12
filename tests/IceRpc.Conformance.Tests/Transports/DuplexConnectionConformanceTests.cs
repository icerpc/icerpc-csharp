// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
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

        // We limit the connection backlog to avoid creating too many connections.
        await using ServiceProvider provider = CreateServiceCollection(listenBacklog: 1)
            .BuildServiceProvider(validateScopes: true);
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
        [Values] bool serverDispose)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();

        Task clientConnectTask = sut.Client.ConnectAsync(default);
        Task serverConnectTask = sut.AcceptAsync(default);
        await (serverDispose ? serverConnectTask : clientConnectTask);

        // Act
        if (serverDispose)
        {
            sut.Server.Dispose();
        }
        else
        {
            sut.Client.Dispose();
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
                Is.Null.Or.EqualTo(IceRpcError.ConnectionAborted).Or.EqualTo(IceRpcError.IceRpcError),
                $"The test failed with an unexpected IceRpcError {exception}");
        }
        else
        {
            Assert.That(
                exception?.IceRpcError,
                Is.Null.Or.EqualTo(IceRpcError.ConnectionAborted),
                $"The test failed with an unexpected IceRpcError {exception}");
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
        var payload = new ReadOnlySequence<byte>(new byte[1024 * 1024]);
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();

        int writtenSize = 0;
        Task writeTask;
        while (true)
        {
            writtenSize += (int)payload.Length;
            writeTask = sut.Client.WriteAsync(payload, default).AsTask();
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
        Task readTask = ReadAsync(sut.Server, writtenSize);

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
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();
        var buffer = new Memory<byte>(new byte[1]);

        Assert.That(
            async () => await sut.Client.ReadAsync(buffer, new CancellationToken(canceled: true)),
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
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();
        ValueTask<int> readTask = sut.Client.ReadAsync(new byte[1], cts.Token);

        // Act
        cts.Cancel();

        // Assert
        Assert.That(async () => await readTask, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that calling read on a connection fails with
    /// <see cref="IceRpcError.ConnectionAborted" /> if the peer connection is disposed.</summary>
    [Test]
    public async Task Read_from_disposed_peer_connection_fails_with_connection_aborted([Values] bool readFromServer)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();

        IDuplexConnection readFrom = readFromServer ? sut.Server : sut.Client;
        IDuplexConnection disposedPeer = readFromServer ? sut.Client : sut.Server;

        disposedPeer.Dispose();

        // Act/Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await readFrom.ReadAsync(new byte[1], default));
        Assert.That(
            exception!.IceRpcError,
            Is.EqualTo(IceRpcError.ConnectionAborted),
            $"The test failed with an unexpected IceRpcError {exception}");
    }

    [Test]
    public async Task Read_on_peer_returns_zero_after_shutdown_write()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();

        // Act
        await sut.Server.ShutdownWriteAsync(CancellationToken.None);
        int clientRead = await sut.Client.ReadAsync(new byte[1], CancellationToken.None);

        await sut.Client.ShutdownWriteAsync(CancellationToken.None);
        int serverRead = await sut.Server.ReadAsync(new byte[1], CancellationToken.None);

        // Assert
        Assert.That(clientRead, Is.EqualTo(0));
        Assert.That(serverRead, Is.EqualTo(0));
    }

    [Test]
    public async Task Shutdown_connection_writes()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();

        // Act/Assert
        Assert.That(
            async () => await sut.Client.ShutdownWriteAsync(CancellationToken.None),
            Throws.Nothing);

        Assert.That(
            async () => await sut.Server.ShutdownWriteAsync(CancellationToken.None),
            Throws.Nothing);
    }

    [Test]
    public async Task Shutdown_connection_writes_on_both_sides()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();

        // Act
        var clientShutdownTask = sut.Client.ShutdownWriteAsync(CancellationToken.None);
        var serverShutdownTask = sut.Server.ShutdownWriteAsync(CancellationToken.None);

        // Assert
        Assert.That(async () => await clientShutdownTask, Throws.Nothing);
        Assert.That(async () => await serverShutdownTask, Throws.Nothing);
    }

    /// <summary>Verifies that we can write and read using the duplex connection.</summary>
    [TestCase(3)]
    [TestCase(11)]
    [TestCase(1024)]
    public async Task Write_and_read_buffers(int size)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();

        using var customPool = new TestMemoryPool(7);
        var pipeOptions = new PipeOptions(customPool, minimumSegmentSize: 5);
        var pipe = new Pipe(pipeOptions);

        Memory<byte> buffer = new byte[size];
        for (int i = 0; i < size; ++i)
        {
            buffer.Span[i] = (byte)(i % 256);
        }
        pipe.Writer.Write(buffer.Span);
        pipe.Writer.Complete();
        _ = pipe.Reader.TryRead(out ReadResult readResult);

        // Act
        ValueTask writeTask = sut.Client.WriteAsync(readResult.Buffer, default);
        Memory<byte> readBuffer = new byte[size];
        int offset = 0;
        while (offset < size)
        {
            offset += await sut.Server.ReadAsync(readBuffer[offset..], default);
        }
        await writeTask;

        // Assert
        Assert.That(offset, Is.EqualTo(size));
        Assert.That(readBuffer.Span.SequenceEqual(buffer.Span), Is.True);

        // Cleanup
        pipe.Reader.Complete();
    }

    [Test]
    public async Task Write_canceled()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();
        var buffer = new ReadOnlySequence<byte>(new byte[1]);

        Assert.That(
            async () => await sut.Client.WriteAsync(buffer, new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that pending write operation fails with <see cref="OperationCanceledException" /> once the
    /// cancellation token is canceled.</summary>
    [Test]
    public async Task Write_cancellation()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();
        var buffer = new ReadOnlySequence<byte>(new byte[1024 * 1024]);
        using var cts = new CancellationTokenSource();

        // Write data until flow control blocks the sending. Canceling the blocked write, should throw
        // OperationCanceledException.
        Task writeTask;
        while (true)
        {
            writeTask = sut.Client.WriteAsync(buffer, cts.Token).AsTask();
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
    public async Task Write_fails_after_shutdown_write()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();
        await sut.Server.ShutdownWriteAsync(CancellationToken.None);

        // Act/Assert
        Assert.That(
            async () => await sut.Server.WriteAsync(new ReadOnlySequence<byte>(new byte[1]),
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
        var sut = provider.GetRequiredService<ClientServerDuplexConnection>();
        await sut.AcceptAndConnectAsync();

        // Act
        sut.Server.Dispose();

        // Assert
        var buffer = new ReadOnlySequence<byte>(new byte[1]);
        IceRpcException exception;
        try
        {
            // It can take few writes to detect the peer's connection closure.
            while (true)
            {
                await sut.Client.WriteAsync(buffer, default);
                await Task.Delay(50);
            }
        }
        catch (IceRpcException ex)
        {
            exception = ex;
        }

        Assert.That(
            exception.IceRpcError,
            Is.EqualTo(IceRpcError.ConnectionAborted),
            $"The test failed with an unexpected IceRpcError {exception}");
    }

    /// <summary>Creates the service collection used for the duplex transport conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection(int? listenBacklog = null);
}
