// Copyright (c) ZeroC, Inc.

using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Security;

namespace IceRpc.Conformance.Tests;

/// <summary>Conformance tests for the multiplexed transports.</summary>
public abstract class MultiplexedConnectionConformanceTests
{
    private static readonly ReadOnlyMemory<byte> _oneBytePayload = new(new byte[] { 0xFF });

    /// <summary>Verifies that both peers can initiate and accept streams.</summary>
    /// <param name="serverInitiated">Whether the stream is initiated by the server or by the client.</param>
    [Test]
    public async Task Accept_a_stream([Values] bool serverInitiated)
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        using var streams = await sut.CreateAndAcceptStreamAsync(createWithServerConnection: serverInitiated);

        Assert.That(streams.Local.Id, Is.EqualTo(streams.Remote.Id));
    }

    /// <summary>Verifies that accept stream calls can be canceled.</summary>
    [Test]
    public async Task Accept_stream_cancellation()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        using var cts = new CancellationTokenSource();
        ValueTask<IMultiplexedStream> acceptTask = sut.Server.AcceptStreamAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(10)); // give a few ms for acceptTask to start

        // Act
        cts.Cancel();

        // Assert
        Assert.That(async () => await acceptTask, Throws.TypeOf<OperationCanceledException>());

        // We also verify we can still create new streams. This shows that canceling AcceptAsync does not "abort" new
        // streams and is a transient cancellation (not obvious with QUIC).
        Assert.That(
            async () =>
            {
                using var streams = await sut.CreateAndAcceptStreamAsync();
            },
            Throws.Nothing);
    }

    /// <summary>Verifies that AcceptStream fails when the connection is closed.</summary>
    [TestCase(MultiplexedConnectionCloseError.NoError, IceRpcError.ConnectionClosedByPeer)]
    [TestCase(MultiplexedConnectionCloseError.Aborted, IceRpcError.ConnectionAborted)]
    [TestCase(MultiplexedConnectionCloseError.Refused, IceRpcError.ConnectionRefused)]
    [TestCase(MultiplexedConnectionCloseError.ServerBusy, IceRpcError.ServerBusy)]
    [TestCase((MultiplexedConnectionCloseError)255, IceRpcError.ConnectionAborted)]
    public async Task Accept_stream_fails_on_close(
        MultiplexedConnectionCloseError closeError,
        IceRpcError expectedIceRpcError)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        Task acceptStreamTask = sut.Server.AcceptStreamAsync(CancellationToken.None).AsTask();

        // Act
        await sut.Client.CloseAsync(closeError, CancellationToken.None);

        // Assert
        IceRpcException ex = Assert.ThrowsAsync<IceRpcException>(async () => await acceptStreamTask)!;
        Assert.That(ex.IceRpcError, Is.EqualTo(expectedIceRpcError));
    }

    [Test]
    public async Task Completing_an_unidirectional_stream_allows_accepting_a_new_one([Values] bool abort)
    {
        // Arrange
        IServiceCollection serviceCollection = CreateServiceCollection();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxUnidirectionalStreams = 1);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        IMultiplexedStream clientStream1 = await sut.Client.CreateStreamAsync(bidirectional: false, default);
        await clientStream1.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream serverStream1 = await sut.Server.AcceptStreamAsync(default);

        ValueTask<IMultiplexedStream> clientStream2Task = sut.Client.CreateStreamAsync(bidirectional: false, default);

        await Task.Delay(50);

        // Act
        bool stream2TaskIsCompleted = clientStream2Task.IsCompleted;

        // Completing the remote input will allow a new stream to be accepted.
        serverStream1.Input.Complete(abort ? new Exception() : null);

        // Assert

        // The second stream wasn't accepted before the first stream completion.
        Assert.That(stream2TaskIsCompleted, Is.False);

        // The second stream is accepted after the first stream completion.
        Assert.That(async () => await clientStream2Task, Throws.Nothing);

        // Cleanup
        clientStream1.Output.Complete();
    }

    [Test]
    public async Task Completing_a_bidirectional_stream_allows_accepting_a_new_one([Values] bool abort)
    {
        // Arrange
        IServiceCollection serviceCollection = CreateServiceCollection();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.MaxBidirectionalStreams = 1);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        IMultiplexedStream clientStream1 = await sut.Client.CreateStreamAsync(bidirectional: true, default);
        await clientStream1.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream serverStream1 = await sut.Server.AcceptStreamAsync(default);

        ValueTask<IMultiplexedStream> clientStream2Task = sut.Client.CreateStreamAsync(bidirectional: true, default);

        await Task.Delay(50);

        // Act
        bool stream2TaskIsCompleted = clientStream2Task.IsCompleted;
        // Completing the remote input and output will allow a new stream to be accepted.
        serverStream1.Input.Complete(abort ? new Exception() : null);
        serverStream1.Output.Complete(abort ? new Exception() : null);

        // Assert

        // The second stream wasn't accepted before the first stream completion.
        Assert.That(stream2TaskIsCompleted, Is.False);

        // The second stream is accepted after the first stream completion.
        Assert.That(async () => await clientStream2Task, Throws.Nothing);

        // Cleanup
        clientStream1.Output.Complete();
        clientStream1.Input.Complete();
    }

    [Test]
    public async Task Cancel_create_stream_which_is_blocked_after_max_streams_has_been_reached()
    {
        // Arrange
        IServiceCollection serviceCollection = CreateServiceCollection();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxBidirectionalStreams = 1);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        IMultiplexedStream clientStream1 = await sut.Client.CreateStreamAsync(bidirectional: true, default);
        await clientStream1.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream serverStream1 = await sut.Server.AcceptStreamAsync(default);
        ReadResult readResult = await serverStream1.Input.ReadAsync();
        serverStream1.Input.AdvanceTo(readResult.Buffer.End);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act/Assert
        Assert.That(
            async () => await sut.Client.CreateStreamAsync(bidirectional: true, cts.Token),
            Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public async Task Create_stream_which_is_waiting_on_streams_semaphore_fails_when_connection_is_closed()
    {
        // Arrange
        IServiceCollection serviceCollection = CreateServiceCollection();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxBidirectionalStreams = 1);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        IMultiplexedStream clientStream1 = await sut.Client.CreateStreamAsync(bidirectional: true, default);
        await clientStream1.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream serverStream1 = await sut.Server.AcceptStreamAsync(default);
        ReadResult readResult = await serverStream1.Input.ReadAsync();
        serverStream1.Input.AdvanceTo(readResult.Buffer.End);

        // Act/Assert
        var createStreamTask = sut.Client.CreateStreamAsync(bidirectional: true, default).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(createStreamTask.IsCompleted, Is.False);
        await sut.Client.CloseAsync(MultiplexedConnectionCloseError.NoError, default);

        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await createStreamTask);
        Assert.That(
            exception?.IceRpcError,
            Is.EqualTo(IceRpcError.OperationAborted),
            $"The test failed with an unexpected IceRpcError {exception}");
    }

    /// <summary>Verify streams cannot be created after closing down the connection.</summary>
    [TestCase(MultiplexedConnectionCloseError.NoError, IceRpcError.ConnectionClosedByPeer)]
    [TestCase(MultiplexedConnectionCloseError.Aborted, IceRpcError.ConnectionAborted)]
    [TestCase(MultiplexedConnectionCloseError.Refused, IceRpcError.ConnectionRefused)]
    [TestCase(MultiplexedConnectionCloseError.ServerBusy, IceRpcError.ServerBusy)]
    [TestCase((MultiplexedConnectionCloseError)255, IceRpcError.ConnectionAborted)]
    public async Task Cannot_create_streams_with_a_closed_connection(
        MultiplexedConnectionCloseError closeError,
        IceRpcError expectedIceRpcError)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        // Act
        await sut.Server.CloseAsync(closeError, CancellationToken.None);

        // Assert
        IceRpcException? exception;

        exception = Assert.ThrowsAsync<IceRpcException>(
            () => sut.Client.AcceptStreamAsync(CancellationToken.None).AsTask());
        Assert.That(exception!.IceRpcError, Is.EqualTo(expectedIceRpcError));

        exception = Assert.ThrowsAsync<IceRpcException>(
            () => sut.Client.CreateStreamAsync(true, default).AsTask());
        Assert.That(exception!.IceRpcError, Is.EqualTo(expectedIceRpcError));
    }

    /// <summary>Verify streams cannot be created after disposing the connection.</summary>
    /// <param name="disposeServerConnection">Whether to dispose the server connection or the client connection.
    /// </param>
    [Test]
    public async Task Cannot_create_streams_with_a_disposed_connection([Values] bool disposeServerConnection)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        IMultiplexedConnection disposedConnection = disposeServerConnection ? sut.Server : sut.Client;
        IMultiplexedConnection peerConnection = disposeServerConnection ? sut.Client : sut.Server;
        IMultiplexedStream peerStream = await peerConnection.CreateStreamAsync(true, default).ConfigureAwait(false);
        await peerStream.Output.WriteAsync(_oneBytePayload); // Make sure the stream is started before DisposeAsync

        // Act
        await disposedConnection.DisposeAsync();

        // Assert

        IceRpcException? exception;

        Assert.ThrowsAsync<ObjectDisposedException>(() => disposedConnection.CreateStreamAsync(true, default).AsTask());

        exception = Assert.ThrowsAsync<IceRpcException>(async () =>
            {
                // It can take few writes for the peer to detect the connection closure.
                while (true)
                {
                    FlushResult result = await peerStream.Output.WriteAsync(_oneBytePayload);
                    Assert.That(result.IsCompleted, Is.False);
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                }
            });

        Assert.That(
            exception!.IceRpcError,
            Is.EqualTo(IceRpcError.ConnectionAborted),
            $"The test failed with an unexpected IceRpcError {exception}");
    }

    [Test]
    public async Task Close_connection()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        // Act/Assert

        Assert.That(async () => await sut.Client.CloseAsync(
            MultiplexedConnectionCloseError.NoError,
            CancellationToken.None), Throws.Nothing);

        Assert.That(async () => await sut.Server.CloseAsync(
            MultiplexedConnectionCloseError.NoError,
            CancellationToken.None), Throws.Nothing);
    }

    [Test]
    public async Task Close_connection_simultaneously_on_both_sides()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        // Act
        Task clientCloseTask = sut.Client.CloseAsync(
            MultiplexedConnectionCloseError.NoError,
            CancellationToken.None);

        Task serverCloseTask = sut.Server.CloseAsync(
            MultiplexedConnectionCloseError.NoError,
            CancellationToken.None);

        // Assert
        Assert.That(() => clientCloseTask, Throws.Nothing);
        Assert.That(() => serverCloseTask, Throws.Nothing);
    }

    [Test]
    public async Task Create_client_connection_with_unknown_server_address_parameter_fails_with_format_exception()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();

        var serverAddress = new ServerAddress(new Uri("icerpc://foo?unknown-parameter=foo"));

        // Act/Asserts
        Assert.Throws<ArgumentException>(() => clientTransport.CreateConnection(
            serverAddress,
            new MultiplexedConnectionOptions(),
            provider.GetService<SslClientAuthenticationOptions>()));
    }

    [Test]
    public async Task Create_server_connection_with_unknown_server_address_parameter_fails_with_format_exception()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var serverTransport = provider.GetRequiredService<IMultiplexedServerTransport>();

        var serverAddress = new ServerAddress(new Uri("icerpc://foo?unknown-parameter=foo"));

        // Act/Asserts
        Assert.Throws<ArgumentException>(() => serverTransport.Listen(
            serverAddress,
            new MultiplexedConnectionOptions(),
            provider.GetService<SslServerAuthenticationOptions>()));
    }

    [Test]
    public async Task Connection_operations_fail_with_connection_aborted_error_after_close()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();
        using var streams = await sut.CreateAndAcceptStreamAsync();

        // Act
        await sut.Client.CloseAsync(MultiplexedConnectionCloseError.NoError, default);

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await streams.Local.Output.WriteAsync(_oneBytePayload));
        Assert.That(
            exception?.IceRpcError,
            Is.EqualTo(IceRpcError.ConnectionAborted),
            $"The test failed with an unexpected IceRpcError {exception}");

        exception = Assert.ThrowsAsync<IceRpcException>(async () => await streams.Local.Input.ReadAsync());
        Assert.That(
            exception?.IceRpcError,
            Is.EqualTo(IceRpcError.ConnectionAborted),
            $"The test failed with an unexpected IceRpcError {exception}");
    }

    [Test]
    public async Task Connection_operations_fail_with_connection_aborted_error_after_dispose()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();
        using var streams = await sut.CreateAndAcceptStreamAsync();

        // Act
        await sut.Client.DisposeAsync();

        // Assert
        Assert.That(
            async () => await sut.Client.CreateStreamAsync(true, default),
            Throws.InstanceOf<ObjectDisposedException>());

        Assert.That(
            async () => await sut.Client.AcceptStreamAsync(default),
            Throws.InstanceOf<ObjectDisposedException>());

        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await streams.Local.Output.WriteAsync(_oneBytePayload));
        Assert.That(
            exception?.IceRpcError,
            Is.EqualTo(IceRpcError.ConnectionAborted),
            $"The test failed with an unexpected IceRpcError {exception}");

        exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await streams.Local.Input.ReadAsync());
        Assert.That(
            exception?.IceRpcError,
            Is.EqualTo(IceRpcError.ConnectionAborted),
            $"The test failed with an unexpected IceRpcError {exception}");
    }

    [Test]
    public async Task Connection_dispose_aborts_pending_operations_with_operation_aborted_error()
    {
        // Arrange
        IServiceCollection serviceCollection = CreateServiceCollection();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxBidirectionalStreams = 1);
        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();
        using var streams = await sut.CreateAndAcceptStreamAsync();

        ValueTask<IMultiplexedStream> acceptStreamTask = sut.Client.AcceptStreamAsync(default);
        ValueTask<IMultiplexedStream> createStreamTask = sut.Client.CreateStreamAsync(true, default);
        ValueTask<ReadResult> readTask = streams.Local.Input.ReadAsync();

        // Act
        await sut.Client.DisposeAsync();

        // Assert

        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await acceptStreamTask);
        Assert.That(
            exception?.IceRpcError,
            Is.EqualTo(IceRpcError.OperationAborted),
            $"The test failed with an unexpected IceRpcError {exception}");

        exception = Assert.ThrowsAsync<IceRpcException>(async () => await createStreamTask);
        Assert.That(
            exception?.IceRpcError,
            Is.EqualTo(IceRpcError.OperationAborted),
            $"The test failed with an unexpected IceRpcError {exception}");

        exception = Assert.ThrowsAsync<IceRpcException>(async () => await readTask);
        Assert.That(
            exception?.IceRpcError,
            Is.EqualTo(IceRpcError.OperationAborted),
            $"The test failed with an unexpected IceRpcError {exception}");
    }

    [Test]
    public async Task Disposing_the_client_connection_aborts_the_server_connection()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        // Act
        await sut.Client.DisposeAsync();

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await sut.Server.AcceptStreamAsync(default));
        Assert.That(
            exception?.IceRpcError,
            Is.EqualTo(IceRpcError.ConnectionAborted),
            $"The test failed with an unexpected IceRpcError {exception}");
    }

    [Test]
    public async Task Disposing_the_connection_closes_the_streams()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();
        using var streams = await sut.CreateAndAcceptStreamAsync();

        // Act
        await sut.Server.DisposeAsync();

        // Assert
        Assert.That(async () => await streams.Remote.WritesClosed, Throws.Nothing);
        Assert.That(async () => await streams.Local.WritesClosed, Throws.Nothing);
    }

    /// <summary>Write data until the transport flow control start blocking, at this point we start a read task and
    /// ensure that this unblocks the pending write calls.</summary>
    [Test]
    public async Task Flow_control()
    {
        // Arrange
        var payload = new byte[1024 * 64];
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        using var streams = await sut.CreateAndAcceptStreamAsync();
        streams.Local.Input.Complete();
        streams.Remote.Output.Complete();

        Task<FlushResult> writeTask;
        while (true)
        {
            writeTask = streams.Local.Output.WriteAsync(payload).AsTask();
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
        Task readTask = ReadAsync(streams.Remote);

        // Assert
        Assert.That(async () => await writeTask, Throws.Nothing);
        streams.Local.Output.Complete();
        Assert.That(async () => await readTask, Throws.Nothing);

        static async Task ReadAsync(IMultiplexedStream stream)
        {
            ReadResult readResult = default;
            while (!readResult.IsCompleted)
            {
                readResult = await stream.Input.ReadAsync();
                stream.Input.AdvanceTo(readResult.Buffer.End);
            }
            stream.Input.Complete();
        }
    }

    [Test]
    public async Task Max_bidirectional_streams([Values] bool abort)
    {
        var serviceCollection = CreateServiceCollection();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.MaxBidirectionalStreams = 1);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        using var streams = await sut.CreateAndAcceptStreamAsync(bidirectional: true);
        var newStreamsTask = sut.CreateAndAcceptStreamAsync(bidirectional: true);

        // Act/Assert

        // Ensure that a new stream can't be created yet.
        Assert.That(
            async () => await newStreamsTask.WaitAsync(TimeSpan.FromMilliseconds(100)),
            Throws.InstanceOf<TimeoutException>());

        Assert.That(
            async () => await newStreamsTask.WaitAsync(TimeSpan.FromMilliseconds(100)),
            Throws.InstanceOf<TimeoutException>());

        // Completing the remote output and input will allow a new stream to be accepted.
        streams.Remote.Output.Complete(abort ? new Exception() : null);
        streams.Remote.Input.Complete(abort ? new Exception() : null);

        using var _ = await newStreamsTask;

        // Cleanup
        streams.Local.Output.Complete();
        streams.Local.Input.Complete();
    }

    /// <summary>Verifies that connection cannot exceed the bidirectional stream max count.</summary>
    [Test]
    public async Task Max_bidirectional_stream_stress_test()
    {
        // Arrange
        const int streamMaxCount = 16;
        const int createStreamCount = 32;

        var serviceCollection = CreateServiceCollection();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.MaxBidirectionalStreams = streamMaxCount);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        const int payloadSize = 16 * 1024;
        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);

        int streamCount = 0;
        int streamCountMax = 0;
        var mutex = new object();

        var streams = new List<IMultiplexedStream>();
        var tasks = new List<Task>();

        for (int i = 0; i < createStreamCount; ++i)
        {
            tasks.Add(ClientReadWriteAsync());
        }

        // Act
        for (int i = 0; i < createStreamCount; ++i)
        {
            tasks.Add(ServerReadWriteAsync(await sut.Server.AcceptStreamAsync(default)));
        }

        // Assert
        await Task.WhenAll(tasks);
        Assert.That(streamCountMax, Is.LessThanOrEqualTo(streamMaxCount));

        async Task ClientReadWriteAsync()
        {
            IMultiplexedStream stream = await sut.Client.CreateStreamAsync(true, default);
            await stream.Output.WriteAsync(payload);
            lock (mutex)
            {
                streams.Add(stream);
                streamCount++;
                streamCountMax = Math.Max(streamCount, streamCountMax);
            }
            stream.Output.Complete();

            while (true)
            {
                ReadResult readResult = await stream.Input.ReadAsync();
                stream.Input.AdvanceTo(readResult.Buffer.End);
                if (readResult.IsCompleted)
                {
                    break;
                }
            }
            stream.Input.Complete();
        }

        async Task ServerReadWriteAsync(IMultiplexedStream stream)
        {
            while (true)
            {
                ReadResult readResult = await stream.Input.ReadAsync();
                stream.Input.AdvanceTo(readResult.Buffer.End);
                if (readResult.IsCompleted)
                {
                    break;
                }
            }
            stream.Input.Complete();

            lock (mutex)
            {
                streamCount--;
            }

            await stream.Output.WriteAsync(payload);
            stream.Output.Complete();
        }
    }

    /// <summary>Verifies that connection cannot exceed the unidirectional stream max count.</summary>
    [Test]
    public async Task Max_unidirectional_stream_stress_test()
    {
        // Arrange
        const int streamMaxCount = 16;
        const int createStreamCount = 32;

        var serviceCollection = CreateServiceCollection();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.MaxUnidirectionalStreams = streamMaxCount);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        const int payloadSize = 16 * 1024;
        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);

        int streamCount = 0;
        int streamCountMax = 0;
        var mutex = new object();

        var streams = new List<IMultiplexedStream>();
        var tasks = new List<Task>();
        for (int i = 0; i < createStreamCount; ++i)
        {
            tasks.Add(ClientWriteAsync());
        }

        // Act
        for (int i = 0; i < createStreamCount; ++i)
        {
            tasks.Add(ServerReadAsync(await sut.Server.AcceptStreamAsync(default)));
        }

        // Assert
        await Task.WhenAll(tasks);
        Assert.That(streamCountMax, Is.LessThanOrEqualTo(streamMaxCount));

        async Task ClientWriteAsync()
        {
            IMultiplexedStream stream = await sut.Client.CreateStreamAsync(false, default);
            await stream.Output.WriteAsync(payload);
            lock (mutex)
            {
                streams.Add(stream);
                streamCount++;
                streamCountMax = Math.Max(streamCount, streamCountMax);
            }

            // It's important to write enough data to ensure that the last stream frame is not received before the
            // receiver starts reading.
            await stream.Output.WriteAsync(payload);
            await stream.Output.WriteAsync(payload);
            await stream.Output.WriteAsync(payload);
            await stream.Output.WriteAsync(payload);

            stream.Output.Complete();
        }

        async Task ServerReadAsync(IMultiplexedStream stream)
        {
            // The stream is terminated as soon as the last frame of the request is received, so we have
            // to decrement the count here before the request receive completes.
            lock (mutex)
            {
                streamCount--;
            }

            ReadResult readResult;
            do
            {
                readResult = await stream.Input.ReadAsync();
                stream.Input.AdvanceTo(readResult.Buffer.End);
            }
            while (!readResult.IsCompleted);

            stream.Input.Complete();
        }
    }

    [TestCase(MultiplexedConnectionCloseError.NoError, IceRpcError.ConnectionClosedByPeer)]
    [TestCase(MultiplexedConnectionCloseError.Aborted, IceRpcError.ConnectionAborted)]
    [TestCase(MultiplexedConnectionCloseError.Refused, IceRpcError.ConnectionRefused)]
    [TestCase(MultiplexedConnectionCloseError.ServerBusy, IceRpcError.ServerBusy)]
    [TestCase((MultiplexedConnectionCloseError)255, IceRpcError.ConnectionAborted)]
    public async Task Pending_accept_stream_fails_with_peer_close_error_code_on_connection_close(
        MultiplexedConnectionCloseError closeError,
        IceRpcError expectedIceRpcError)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        ValueTask<IMultiplexedStream> acceptStreamTask = sut.Client.AcceptStreamAsync(CancellationToken.None);

        // Act
        await sut.Server.CloseAsync(closeError, CancellationToken.None);

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await acceptStreamTask);
        Assert.That(exception?.IceRpcError, Is.EqualTo(expectedIceRpcError));
    }

    /// <summary>Verify streams cannot be created after closing down the connection.</summary>
    [TestCase(MultiplexedConnectionCloseError.NoError, IceRpcError.ConnectionClosedByPeer)]
    [TestCase(MultiplexedConnectionCloseError.Aborted, IceRpcError.ConnectionAborted)]
    [TestCase(MultiplexedConnectionCloseError.Refused, IceRpcError.ConnectionRefused)]
    [TestCase(MultiplexedConnectionCloseError.ServerBusy, IceRpcError.ServerBusy)]
    [TestCase((MultiplexedConnectionCloseError)255, IceRpcError.ConnectionAborted)]
    public async Task Pending_create_stream_fails_with_peer_close_error_code_on_connection_close(
        MultiplexedConnectionCloseError closeError,
        IceRpcError expectedIceRpcError)
    {
        // Arrange
        IServiceCollection serviceCollection = CreateServiceCollection();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxBidirectionalStreams = 1);
        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        IMultiplexedStream stream1 = await sut.Client.CreateStreamAsync(true, default);
        await stream1.Output.WriteAsync(_oneBytePayload, default); // Ensures the stream is started.

        ValueTask<IMultiplexedStream> stream2CreateStreamTask = sut.Client.CreateStreamAsync(true, default);
        await Task.Delay(100);
        Assert.That(stream2CreateStreamTask.IsCompleted, Is.False);

        // Act
        await sut.Server.CloseAsync(closeError, CancellationToken.None);

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await stream2CreateStreamTask);
        Assert.That(exception?.IceRpcError, Is.EqualTo(expectedIceRpcError));

        stream1.Input.Complete();
        stream1.Output.Complete();
    }

    /// <summary>This test verifies that stream flow control prevents a new stream from being created under the
    /// following conditions:
    ///
    /// - the max stream count is reached
    /// - writes are closed on the remote streams
    /// - data on the remote stream is not consumed</summary>
    [Test]
    public async Task Stream_creation_hangs_until_remote_data_is_consumed()
    {
        // Arrange
        IServiceCollection serviceCollection = CreateServiceCollection();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.MaxBidirectionalStreams = 1);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        IMultiplexedStream clientStream1 = await sut.Client.CreateStreamAsync(bidirectional: true, default);
        await clientStream1.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream serverStream1 = await sut.Server.AcceptStreamAsync(default);
        serverStream1.Output.Complete();

        // At this point the server stream input is not completed. CreateStreamAsync should block because the data isn't
        // consumed.

        // Act/Assert
        Assert.That(
            async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

                // This should throw OperationCanceledException because reads from the remote stream accepted above are
                // not closed (the remote stream still has non-consumed buffered data).
                _ = await sut.Client.CreateStreamAsync(bidirectional: true, cts.Token);
            },
            Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Creates the service collection used for multiplexed transport conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();
}
