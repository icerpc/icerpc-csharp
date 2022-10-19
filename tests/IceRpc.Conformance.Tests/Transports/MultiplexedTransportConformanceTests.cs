// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Security;

namespace IceRpc.Conformance.Tests;

/// <summary>Conformance tests for the multiplexed transports.</summary>
public abstract class MultiplexedTransportConformanceTests
{
    private static readonly ReadOnlyMemory<byte> _oneBytePayload = new(new byte[] { 0xFF });

    /// <summary>Verifies that both peers can initiate and accept streams.</summary>
    /// <param name="serverInitiated">Whether the stream is initiated by the server or by the client.</param>
    [Test]
    public async Task Accept_a_stream([Values(true, false)] bool serverInitiated)
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);

        IMultiplexedConnection clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        (IMultiplexedStream localStream, IMultiplexedStream remoteStream) = await CreateAndAcceptStreamAsync(
            serverInitiated ? serverConnection : clientConnection,
            serverInitiated ? clientConnection : serverConnection);

        Assert.That(localStream.Id, Is.EqualTo(remoteStream.Id));

        await CompleteStreamAsync(remoteStream);
        await CompleteStreamAsync(localStream);
    }

    /// <summary>Verifies that no new streams can be accepted after the connection is closed.</summary>
    [Test]
    public async Task Accepting_a_stream_fails_after_close()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        IMultiplexedConnection clientConnection =
            provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        Task acceptStreams = serverConnection.AcceptStreamAsync(CancellationToken.None).AsTask();

        // Act
        await clientConnection.CloseAsync(applicationErrorCode: 2ul, CancellationToken.None);

        // Assert
        TransportException ex = Assert.ThrowsAsync<TransportException>(async () => await acceptStreams)!;
        Assert.Multiple(() =>
        {
            Assert.That(ex.ErrorCode, Is.EqualTo(TransportErrorCode.ConnectionClosed));
            Assert.That(ex.ApplicationErrorCode, Is.EqualTo(2ul));
        });
    }

    /// <summary>Verifies that accept stream calls can be canceled.</summary>
    [Test]
    public async Task Accept_stream_cancellation()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        using var cts = new CancellationTokenSource();
        ValueTask<IMultiplexedStream> acceptTask = serverConnection.AcceptStreamAsync(cts.Token);

        // Act
        cts.Cancel();

        // Assert
        Assert.That(async () => await acceptTask, Throws.TypeOf<OperationCanceledException>());
    }

    /// <summary>Verifies that after reaching the stream max count, new streams are not accepted until a
    /// stream is closed.</summary>
    /// <param name="streamMaxCount">The max stream count limit to use for the test.</param>
    /// <param name="bidirectional">Whether to test with bidirectional or unidirectional streams.</param>
    [Test]
    public async Task After_reach_max_stream_count_completing_a_stream_allows_accepting_a_new_one(
       [Values(1, 1024)] int streamMaxCount,
       [Values(true, false)] bool bidirectional)
    {
        // Arrange
        IServiceCollection serviceCollection = CreateServiceCollection().AddMultiplexedTransportTest();
        if (bidirectional)
        {
            serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxBidirectionalStreams = streamMaxCount);
        }
        else
        {
            serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxUnidirectionalStreams = streamMaxCount);
        }
        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        List<IMultiplexedStream> streams = await CreateStreamsAsync(streamMaxCount, bidirectional);

        Task<IMultiplexedStream> lastStreamTask = CreateLastStreamAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        IMultiplexedStream serverStream = await serverConnection.AcceptStreamAsync(default);
        if (bidirectional)
        {
            await serverStream.Output.CompleteAsync(new OperationCanceledException());
        }
        bool isCompleted = lastStreamTask.IsCompleted;

        // Act
        await serverStream.Input.CompleteAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(isCompleted, Is.False);
            Assert.That(async () => await lastStreamTask, Throws.Nothing);
        });

        await CompleteStreamsAsync(streams);
        await CompleteStreamAsync(await lastStreamTask);

        async Task<IMultiplexedStream> CreateLastStreamAsync()
        {
            ValueTask<IMultiplexedStream> lastStreamTask = clientConnection.CreateStreamAsync(bidirectional, default);
            IMultiplexedStream lastStream;
            if (lastStreamTask.IsCompleted)
            {
                // With Slic the stream creation always completes even if the max stream count is reached. The stream
                // count is only checked on the first write.
                // TODO: Fix Slic implementation to block on CreateStreamAsync
                lastStream = lastStreamTask.Result;
                await lastStream.Output.WriteAsync(_oneBytePayload, default);
            }
            else
            {
                lastStream = await lastStreamTask;
            }
            return lastStream;
        }

        async Task<List<IMultiplexedStream>> CreateStreamsAsync(int count, bool bidirectional)
        {
            var streams = new List<IMultiplexedStream>();
            for (int i = 0; i < count; i++)
            {
                IMultiplexedStream stream = await clientConnection.CreateStreamAsync(
                    bidirectional,
                    default).ConfigureAwait(false);
                streams.Add(stream);
                await stream.Output.WriteAsync(_oneBytePayload, default);
            }
            return streams;
        }
    }

    /// <summary>Verify streams cannot be created after closing down the connection.</summary>
    /// <param name="closeServerConnection">Whether to close the server connection or the client connection.
    /// </param>
    [Test]
    public async Task Cannot_create_streams_with_a_closed_connection(
        [Values(true, false)] bool closeServerConnection)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        IMultiplexedConnection closeConnection =
            closeServerConnection ? serverConnection : clientConnection;
        IMultiplexedConnection peerConnection =
            closeServerConnection ? clientConnection : serverConnection;

        await closeConnection.CloseAsync(applicationErrorCode: 5ul, CancellationToken.None);

        TransportException? exception;

        // Act/Assert
        exception = Assert.ThrowsAsync<TransportException>(
            () => peerConnection.AcceptStreamAsync(CancellationToken.None).AsTask());
        Assert.Multiple(() =>
        {
            Assert.That(exception!.ErrorCode, Is.EqualTo(TransportErrorCode.ConnectionClosed));
            Assert.That(exception!.ApplicationErrorCode, Is.EqualTo(5ul));
        });
        exception = Assert.ThrowsAsync<TransportException>(
            () => peerConnection.CreateStreamAsync(true, default).AsTask());
        Assert.Multiple(() =>
        {
            Assert.That(exception!.ErrorCode, Is.EqualTo(TransportErrorCode.ConnectionClosed));
            Assert.That(exception!.ApplicationErrorCode, Is.EqualTo(5ul));
        });
    }

    /// <summary>Verify streams cannot be created after disposing the connection.</summary>
    /// <param name="disposeServerConnection">Whether to dispose the server connection or the client connection.
    /// </param>
    [Test]
    public async Task Cannot_create_streams_with_a_disposed_connection(
        [Values(true, false)] bool disposeServerConnection)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        IMultiplexedConnection disposedConnection = disposeServerConnection ? serverConnection : clientConnection;
        IMultiplexedConnection peerConnection = disposeServerConnection ? clientConnection : serverConnection;
        IMultiplexedStream peerStream = await peerConnection.CreateStreamAsync(true, default).ConfigureAwait(false);
        await peerStream.Output.WriteAsync(_oneBytePayload); // Make sure the stream is started before DisposeAsync

        // Act
        await disposedConnection.DisposeAsync();

        // Assert

        TransportException? exception;

        Assert.ThrowsAsync<ObjectDisposedException>(() => disposedConnection.CreateStreamAsync(true, default).AsTask());

        exception = Assert.ThrowsAsync<TransportException>(async () =>
            {
                // It can take few writes for the peer to detect the connection closure.
                while (true)
                {
                    FlushResult result = await peerStream.Output.WriteAsync(_oneBytePayload);
                    if (result.IsCompleted)
                    {
                        return;
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                }
            });
        Assert.That(exception!.ErrorCode, Is.EqualTo(TransportErrorCode.ConnectionReset));
    }

    /// <summary>Verifies that ConnectAsync can be canceled.</summary>
    [Test]
    public async Task Connect_cancellation()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);

        using var cts = new CancellationTokenSource();
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var connectTask = clientConnection.ConnectAsync(cts.Token);

        // Act
        cts.Cancel();

        // Assert
        Assert.That(async () => await connectTask, Throws.InstanceOf<OperationCanceledException>());
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
        Assert.That(async () => await connectTask, Throws.InstanceOf<TransportException>());

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

    /// <summary>Verifies that completing a stream with unflushed bytes fails with
    /// <see cref="NotSupportedException" />.</summary>
    [Test]
    public async Task Complete_stream_with_unflushed_bytes_fails()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);
        IMultiplexedStream stream = await clientConnection.CreateStreamAsync(
            bidirectional: true,
            default).ConfigureAwait(false);

        Memory<byte> buffer = stream.Output.GetMemory();
        stream.Output.Advance(buffer.Length);

        Assert.That(async () => await stream.Output.CompleteAsync(), Throws.TypeOf<NotSupportedException>());

        await stream.Input.CompleteAsync();
    }

    /// <summary>Ensures that completing the stream output after writing data doesn't discard the data. A successful
    /// write doesn't imply that the data is actually sent by the underlying transport. The completion of the stream
    /// output should make sure that this data buffered by the underlying transport is not discarded.</summary>
    [Test]
    public async Task Complete_stream_output_after_write_does_not_discard_data()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        (IMultiplexedStream localStream, IMultiplexedStream remoteStream) =
            await CreateAndAcceptStreamAsync(clientConnection, serverConnection);

        byte[] buffer = new byte[512 * 1024];

        // Act
        _ = WriteDataAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(remoteStream.ReadsClosed.IsCompleted, Is.False);
            Assert.That(async () => await ReadDataAsync(), Is.EqualTo(buffer.Length));
            Assert.That(async () => await remoteStream.ReadsClosed, Throws.Nothing);
        });

        await CompleteStreamAsync(localStream);
        await CompleteStreamAsync(remoteStream);

        async Task<int> ReadDataAsync()
        {
            ReadResult readResult;
            int readLength = 0;
            do
            {
                readResult = await remoteStream.Input.ReadAsync(default);
                readLength += (int)readResult.Buffer.Length;
                remoteStream.Input.AdvanceTo(readResult.Buffer.End);
            }
            while (!remoteStream.ReadsClosed.IsCompleted);
            return readLength;
        }

        async Task WriteDataAsync()
        {
            // Send a large buffer to ensure the transport (eventually) buffers the sending of the data.
            await localStream.Output.WriteAsync(buffer);

            // Act
            localStream.Output.Complete();
        }
    }

    /// <summary>Verifies that disabling the idle timeout doesn't abort the connection if it's idle.</summary>
    [Test]
    public async Task Connection_with_no_idle_timeout_is_not_aborted_when_idle()
    {
        // Arrange
        IServiceCollection services = CreateServiceCollection();

        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.IdleTimeout = Timeout.InfiniteTimeSpan);
        services.AddOptions<SlicTransportOptions>("client").Configure(
            options => options.IdleTimeout = Timeout.InfiniteTimeSpan);

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();

        await using var clientConnection = clientTransport.CreateConnection(
            listener.ServerAddress,
            provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
            provider.GetService<SslClientAuthenticationOptions>());

        var connectTask = clientConnection.ConnectAsync(default);
        await using var serverConnection = (await listener.AcceptAsync(default)).Connection;

        _ = await serverConnection.ConnectAsync(default);
        _ = await connectTask;

        ValueTask<IMultiplexedStream> acceptTask = serverConnection.AcceptStreamAsync(default);

        // Act
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert
        Assert.That(acceptTask.IsCompleted, Is.False);
    }

    /// <summary>Verifies that setting the idle timeout doesn't abort the connection if it's idle.</summary>
    [Test]
    public async Task Connection_with_idle_timeout_is_not_aborted_when_idle(
        [Values(true, false)] bool serverIdleTimeout)
    {
        // Arrange
        IServiceCollection services = CreateServiceCollection();

        var idleTimeout = TimeSpan.FromSeconds(1);
        if (serverIdleTimeout)
        {
            services.AddOptions<SlicTransportOptions>("server").Configure(options => options.IdleTimeout = idleTimeout);
        }
        else
        {
            services.AddOptions<SlicTransportOptions>("client").Configure(options => options.IdleTimeout = idleTimeout);
        }

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
        await using var clientConnection = clientTransport.CreateConnection(
            listener.ServerAddress,
            provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
            provider.GetService<SslClientAuthenticationOptions>());

        var connectTask = clientConnection.ConnectAsync(default);
        await using var serverConnection = (await listener.AcceptAsync(default)).Connection;

        _ = await serverConnection.ConnectAsync(default);
        _ = await connectTask;

        ValueTask<IMultiplexedStream> acceptTask = serverConnection.AcceptStreamAsync(default);

        // Act
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert
        Assert.That(acceptTask.IsCompleted, Is.False);
    }

    /// <summary>Verifies that disposing the connection aborts the streams.</summary>
    /// <param name="disposeServer">Whether to dispose the server connection or the client connection.
    /// </param>
    [Test]
    public async Task Disposing_the_connection_aborts_the_streams([Values(true, false)] bool disposeServer)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        IMultiplexedConnection disposedConnection = disposeServer ? serverConnection : clientConnection;
        (IMultiplexedStream localStream, IMultiplexedStream remoteStream) =
            await CreateAndAcceptStreamAsync(clientConnection, serverConnection);

        IMultiplexedStream disposedStream = disposeServer ? remoteStream : localStream;
        IMultiplexedStream peerStream = disposeServer ? localStream : remoteStream;

        // Act
        await disposedConnection.DisposeAsync();

        // Assert

        Assert.ThrowsAsync<TransportException>(async () => await disposedStream.Input.ReadAsync());
        Assert.ThrowsAsync<TransportException>(async () => await disposedStream.Output.WriteAsync(_oneBytePayload));

        Assert.ThrowsAsync<TransportException>(async () => await peerStream.Input.ReadAsync());
        Assert.ThrowsAsync<TransportException>(async () => await peerStream.Output.WriteAsync(_oneBytePayload));

        await CompleteStreamAsync(localStream);
        await CompleteStreamAsync(remoteStream);
    }

    [Test]
    public async Task Disposing_the_connection_completes_the_streams()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        (IMultiplexedStream localStream, IMultiplexedStream remoteStream) =
            await CreateAndAcceptStreamAsync(clientConnection, serverConnection);

        // Act
        await serverConnection.DisposeAsync();

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(async () => await localStream.ReadsClosed, Throws.TypeOf<TransportException>());
            Assert.That(async () => await localStream.WritesClosed, Throws.TypeOf<TransportException>());
            Assert.That(async () => await remoteStream.ReadsClosed, Throws.TypeOf<TransportException>());
            Assert.That(async () => await remoteStream.WritesClosed, Throws.TypeOf<TransportException>());
        });
        await CompleteStreamAsync(localStream);
        await CompleteStreamAsync(remoteStream);
    }

    /// <summary>Write data until the transport flow control start blocking, at this point we start a read task and
    /// ensure that this unblocks the pending write calls.</summary>
    [Test]
    public async Task Flow_control()
    {
        // Arrange
        var payload = new byte[1024 * 64];
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        var sut = await CreateAndAcceptStreamAsync(clientConnection, serverConnection);
        await sut.LocalStream.Input.CompleteAsync();
        await sut.RemoteStream.Output.CompleteAsync();

        Task<FlushResult> writeTask;
        while (true)
        {
            writeTask = sut.LocalStream.Output.WriteAsync(payload).AsTask();
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
        Task readTask = ReadAsync(sut.RemoteStream);

        // Assert
        Assert.That(async () => await writeTask, Throws.Nothing);
        await sut.LocalStream.Output.CompleteAsync();
        Assert.That(async () => await readTask, Throws.Nothing);

        static async Task ReadAsync(IMultiplexedStream stream)
        {
            ReadResult readResult = default;
            while (!readResult.IsCompleted)
            {
                readResult = await stream.Input.ReadAsync();
                stream.Input.AdvanceTo(readResult.Buffer.End);
            }
            await stream.Input.CompleteAsync();
        }
    }

    /// <summary>Verifies that connection cannot exceed the bidirectional stream max count.</summary>
    [Test]
    public async Task Max_bidirectional_stream_stress_test()
    {
        // Arrange
        const int streamMaxCount = 16;
        const int createStreamCount = 32;

        var serviceCollection = CreateServiceCollection().AddMultiplexedTransportTest();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxBidirectionalStreams = streamMaxCount);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        const int payloadSize = 16 * 1024;
        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);

        int streamCount = 0;
        int streamCountMax = 0;
        object mutex = new object();

        var streams = new List<IMultiplexedStream>();
        var tasks = new List<Task>();

        for (int i = 0; i < createStreamCount; ++i)
        {
            tasks.Add(ClientReadWriteAsync());
        }

        // Act
        for (int i = 0; i < createStreamCount; ++i)
        {
            tasks.Add(ServerReadWriteAsync(await serverConnection.AcceptStreamAsync(default)));
        }

        // Assert
        await Task.WhenAll(tasks);
        Assert.That(streamCountMax, Is.LessThanOrEqualTo(streamMaxCount));

        await CompleteStreamsAsync(streams);

        async Task ClientReadWriteAsync()
        {
            IMultiplexedStream stream = await clientConnection.CreateStreamAsync(true, default);
            streams.Add(stream);
            await stream.Output.WriteAsync(payload);
            lock (mutex)
            {
                streamCount++;
                streamCountMax = Math.Max(streamCount, streamCountMax);
            }
            await stream.Output.CompleteAsync();

            while (true)
            {
                ReadResult readResult = await stream.Input.ReadAsync();
                if (readResult.IsCompleted)
                {
                    stream.Input.AdvanceTo(readResult.Buffer.End);
                    break;
                }
                stream.Input.AdvanceTo(readResult.Buffer.End);
            }
            await stream.Input.CompleteAsync();
        }

        async Task ServerReadWriteAsync(IMultiplexedStream stream)
        {
            while (true)
            {
                ReadResult readResult = await stream.Input.ReadAsync();
                if (readResult.IsCompleted)
                {
                    stream.Input.AdvanceTo(readResult.Buffer.End);
                    break;
                }
                stream.Input.AdvanceTo(readResult.Buffer.End);
            }
            await stream.Input.CompleteAsync();

            lock (mutex)
            {
                streamCount--;
            }

            await stream.Output.WriteAsync(payload);
            await stream.Output.CompleteAsync();
        }
    }

    /// <summary>Verifies that connection cannot exceed the unidirectional stream max count.</summary>
    [Test]
    public async Task Max_unidirectional_stream_stress_test()
    {
        // Arrange
        const int streamMaxCount = 16;
        const int createStreamCount = 32;

        var serviceCollection = CreateServiceCollection().AddMultiplexedTransportTest();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxUnidirectionalStreams = streamMaxCount);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        const int payloadSize = 16 * 1024;
        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);

        int streamCount = 0;
        int streamCountMax = 0;
        object mutex = new object();

        var streams = new List<IMultiplexedStream>();
        var tasks = new List<Task>();
        for (int i = 0; i < createStreamCount; ++i)
        {
            tasks.Add(ClientWriteAsync());
        }

        // Act
        for (int i = 0; i < createStreamCount; ++i)
        {
            tasks.Add(ServerReadAsync(await serverConnection.AcceptStreamAsync(default)));
        }

        // Assert
        await Task.WhenAll(tasks);
        Assert.That(streamCountMax, Is.LessThanOrEqualTo(streamMaxCount));

        await CompleteStreamsAsync(streams);

        async Task ClientWriteAsync()
        {
            IMultiplexedStream stream = await clientConnection.CreateStreamAsync(false, default);
            streams.Add(stream);
            await stream.Output.WriteAsync(payload);
            lock (mutex)
            {
                streamCount++;
                streamCountMax = Math.Max(streamCount, streamCountMax);
            }

            // It's important to write enough data to ensure that the last stream frame is not received before the
            // receiver starts reading.
            await stream.Output.WriteAsync(payload);
            await stream.Output.WriteAsync(payload);
            await stream.Output.WriteAsync(payload);
            await stream.Output.WriteAsync(payload);

            await stream.Output.CompleteAsync();
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

            await stream.Input.CompleteAsync();
        }
    }

    [TestCase(100)]
    [TestCase(15)]
    public async Task Stream_abort_read(int errorCode)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        var sut = await CreateAndAcceptStreamAsync(clientConnection, serverConnection);

        // Act
        await sut.RemoteStream.Input.CompleteAsync(new IceRpcProtocolStreamException((IceRpcStreamErrorCode)errorCode));

        // Assert
        IceRpcProtocolStreamException? ex = Assert.CatchAsync<IceRpcProtocolStreamException>(
            async () =>
            {
                while (true)
                {
                    FlushResult result = await sut.LocalStream.Output.WriteAsync(new byte[1024]);
                    if (result.IsCompleted)
                    {
                        return;
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                }
            });
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.ErrorCode, Is.EqualTo((IceRpcStreamErrorCode)errorCode));

        // Complete the pipe readers/writers to complete the stream.
        await CompleteStreamsAsync(sut);
    }

    [TestCase(100)]
    [TestCase(15)]
    public async Task Stream_abort_write(int errorCode)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        var sut = await CreateAndAcceptStreamAsync(clientConnection, serverConnection);

        // Act
        await sut.LocalStream.Output.CompleteAsync(new IceRpcProtocolStreamException((IceRpcStreamErrorCode)errorCode));

        // Assert
        // Wait for the peer to receive the StreamStopSending/StreamReset frame.
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        IceRpcProtocolStreamException? ex = Assert.CatchAsync<IceRpcProtocolStreamException>(
            async () => await sut.RemoteStream.Input.ReadAsync());
        Assert.That(ex, Is.Not.Null);
        Assert.That(ex!.ErrorCode, Is.EqualTo((IceRpcStreamErrorCode)errorCode));

        // Complete the pipe readers/writers to complete the stream.
        await CompleteStreamsAsync(sut);
    }

    /// <summary>Verifies that we can read and write concurrently to multiple streams.</summary>
    /// <param name="delay">Number of milliseconds to delay the read and write operation.</param>
    /// <param name="streams">The number of streams to create.</param>
    /// <param name="segments">The number of segments to write to each stream.</param>
    /// <param name="payloadSize">The payload size to write with each write call.</param>
    [Test]
    public async Task Stream_full_duplex_communication(
        [Values(0, 5)] int delay,
        [Values(1, 16)] int streams,
        [Values(1, 32)] int segments,
        [Values(1, 16 * 1024)] int payloadSize)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        var clientStreams = new IMultiplexedStream[streams];
        var serverStreams = new IMultiplexedStream[streams];

        for (int i = 0; i < streams; ++i)
        {
            var sut = await CreateAndAcceptStreamAsync(clientConnection, serverConnection);
            clientStreams[i] = sut.LocalStream;
            serverStreams[i] = sut.RemoteStream;
        }

        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);

        var writeTasks = new List<Task>();
        var readTasks = new List<Task<byte[]>>();

        // Act
        for (int i = 0; i < streams; ++i)
        {
            writeTasks.Add(WriteAsync(clientStreams[i], segments, payload));
            readTasks.Add(ReadAsync(serverStreams[i], payloadSize * segments));
            writeTasks.Add(WriteAsync(serverStreams[i], segments, payload));
            readTasks.Add(ReadAsync(clientStreams[i], payloadSize * segments));
        }

        // Assert
        await Task.WhenAll(writeTasks.Concat(readTasks));

        foreach (Task<byte[]> readTask in readTasks)
        {
            var readResult = new ArraySegment<byte>(await readTask);
            for (int i = 0; i < segments; ++i)
            {
                Assert.That(
                    readResult.Slice(
                        i * payload.Length,
                        payload.Length).AsMemory().Span.SequenceEqual(new ReadOnlySpan<byte>(payloadData)),
                    Is.True);
            }
        }

        async Task<byte[]> ReadAsync(IMultiplexedStream stream, long size)
        {
            while (true)
            {
                // wait for delay
                ReadResult result = await stream.Input.ReadAsync();
                if (delay > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delay));
                }
                if (result.Buffer.Length == size)
                {
                    byte[] buffer = result.Buffer.ToArray();
                    stream.Input.AdvanceTo(result.Buffer.End);
                    return buffer;
                }
                else
                {
                    stream.Input.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                }
            }
        }

        async Task WriteAsync(IMultiplexedStream stream, int segments, ReadOnlyMemory<byte> payload)
        {
            for (int i = 0; i < segments; ++i)
            {
                if (delay > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delay));
                }
                await stream.Output.WriteAsync(payload, default);
            }
            await stream.Output.CompleteAsync();
        }
    }

    /// <summary>Verifies that the input pipe reader keeps not consumed data around and is still accessible in
    /// subsequent read calls.</summary>
    /// <param name="segments">The number of segments to write to the stream.</param>
    /// <param name="payloadSize">The size of the payload in bytes.</param>
    [Test]
    public async Task Stream_read_examine_data_without_consuming(
        [Values(64, 256)] int segments,
        [Values(1024, 8192)] int payloadSize)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        var sut = await CreateAndAcceptStreamAsync(clientConnection, serverConnection);
        await sut.RemoteStream.Output.CompleteAsync();

        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);
        Task writeTask = WriteAsync(sut.LocalStream, segments, payload);

        // Act
        Task<byte[]> readTask = ReadAsync(sut.RemoteStream, payloadSize * segments);

        // Assert
        await Task.WhenAll(writeTask, readTask);

        var readResult = new ArraySegment<byte>(await readTask);
        for (int i = 0; i < segments; ++i)
        {
            Assert.That(
                readResult.Slice(
                    i * payload.Length,
                    payload.Length).AsMemory().Span.SequenceEqual(new ReadOnlySpan<byte>(payloadData)),
                Is.True);
        }

        async Task<byte[]> ReadAsync(IMultiplexedStream stream, long size)
        {
            byte[] buffer = Array.Empty<byte>();
            while (buffer.Length == 0)
            {
                ReadResult readResult = await stream.Input.ReadAsync();
                long bufferLength = readResult.Buffer.Length;
                stream.Input.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
                if (bufferLength == size)
                {
                    buffer = readResult.Buffer.ToArray();
                }
            }
            await stream.Input.CompleteAsync();
            return buffer;
        }

        async Task WriteAsync(IMultiplexedStream stream, int segments, ReadOnlyMemory<byte> payload)
        {
            for (int i = 0; i < segments; ++i)
            {
                await stream.Output.WriteAsync(payload, default);
                await Task.Yield();
            }
            await stream.Output.CompleteAsync();
        }
    }

    [Test]
    public async Task Stream_local_input_read_returns_completed_read_result_when_remote_output_is_completed()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        var sut = await CreateAndAcceptStreamAsync(clientConnection, serverConnection);

        // Act
        sut.LocalStream.Output.Complete();

        // Assert
        ReadResult readResult;
        while(!(readResult = await sut.RemoteStream.Input.ReadAsync()).IsCompleted)
        {
            sut.RemoteStream.Input.AdvanceTo(readResult.Buffer.End);
            // Wait for ReadResult.IsCompleted=true
            await Task.Delay(1);
        }

        await CompleteStreamsAsync(sut);
    }

    [Test]
    public async Task Stream_local_output_write_returns_completed_flush_result_when_remote_input_is_completed()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        var sut = await CreateAndAcceptStreamAsync(clientConnection, serverConnection);

        // Act
        sut.LocalStream.Input.Complete();

        // Assert
        while (!(await sut.RemoteStream.Output.WriteAsync(new byte[1])).IsCompleted)
        {
            // Wait for FlushResult.IsCompleted=true
            await Task.Delay(1);
        }

        await CompleteStreamsAsync(sut);
    }

    [Test]
    public async Task Stream_local_output_flush_returns_completed_flush_result_when_remote_input_is_completed()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        var sut = await CreateAndAcceptStreamAsync(clientConnection, serverConnection);
        Memory<byte> _ = sut.RemoteStream.Output.GetMemory();
        sut.RemoteStream.Output.Advance(1);

        // Act
        sut.LocalStream.Input.Complete();

        // Assert
        while (!(await sut.RemoteStream.Output.FlushAsync()).IsCompleted)
        {
            // Wait for FlushResult.IsCompleted=true
            await Task.Delay(1);
        }

        await CompleteStreamsAsync(sut);
    }

    /// <summary>Ensures that reads are closed when the peer completes its output and only once ReadAsync returns a
    /// result with IsCompleted=true.</summary>
    [Test]
    public async Task Stream_reads_closed_after_completing_peer_output([Values(false, true)] bool isBidirectional)
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        (IMultiplexedStream localStream, IMultiplexedStream remoteStream) =
            await CreateAndAcceptStreamAsync(clientConnection, serverConnection, isBidirectional);

        // Act
        localStream.Output.Complete();
        ReadResult readResult = await remoteStream.Input.ReadAsync(default);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(readResult.IsCompleted , Is.True);
            remoteStream.Input.AdvanceTo(readResult.Buffer.End);
            Assert.That(async () => await remoteStream.ReadsClosed, Throws.Nothing);
        });

        await CompleteStreamAsync(localStream);
        await CompleteStreamAsync(remoteStream);
    }

    /// <summary>Verifies that stream output completes after the peer completes the input.</summary>
    [Test]
    public async Task Stream_output_completes_after_completing_peer_input()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        var sut = await CreateAndAcceptStreamAsync(clientConnection, serverConnection);
        await sut.LocalStream.Input.CompleteAsync();
        await sut.RemoteStream.Output.CompleteAsync();

        Task writeTask = WriteAsync(sut.LocalStream);
        ReadResult readResult = await sut.RemoteStream.Input.ReadAsync();
        sut.RemoteStream.Input.AdvanceTo(readResult.Buffer.End);

        // Act
        await sut.RemoteStream.Input.CompleteAsync();

        // Assert
        Assert.That(async () => await writeTask, Throws.Nothing);

        static async Task WriteAsync(IMultiplexedStream stream)
        {
            var payload = new ReadOnlyMemory<byte>(new byte[1024]);
            FlushResult flushResult = default;
            while (!flushResult.IsCompleted)
            {
                flushResult = await stream.Output.WriteAsync(payload);
                await Task.Delay(TimeSpan.FromMilliseconds(20));
            }
            await stream.Output.CompleteAsync();
        }
    }

    /// <summary>Verifies that calling read with a canceled cancellation token fails with
    /// <see cref="OperationCanceledException" />.</summary>
    [Test]
    public async Task Stream_read_with_canceled_token_fails()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        IMultiplexedStream clientStream = await clientConnection.CreateStreamAsync(
            bidirectional: true,
            default).ConfigureAwait(false);

        // Act/Assert
        Assert.CatchAsync<OperationCanceledException>(
            async () => await clientStream.Input.ReadAsync(new CancellationToken(canceled: true)));
    }

    /// <summary>Verifies that stream read can be canceled.</summary>
    [Test]
    public async Task Stream_read_cancellation()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        IMultiplexedStream clientStream = await clientConnection.CreateStreamAsync(
            bidirectional: true,
            default).ConfigureAwait(false);
        using var cts = new CancellationTokenSource();
        ValueTask<ReadResult> readTask = clientStream.Input.ReadAsync(cts.Token);

        // Act
        cts.Cancel();

        // Assert
        Assert.CatchAsync<OperationCanceledException>(async () => await readTask);
    }

    /// <summary>Verifies that aborting the stream cancels a pending read.</summary>
    [Test]
    public async Task Stream_abort_cancels_read()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        IMultiplexedStream clientStream = await clientConnection.CreateStreamAsync(
            bidirectional: true,
            default).ConfigureAwait(false);
        ValueTask<ReadResult> task = clientStream.Input.ReadAsync(CancellationToken.None);

        // Act
        clientStream.Abort(new InvalidOperationException());

        // Act/Assert
        Assert.CatchAsync<InvalidOperationException>(async () => await task);
    }

    /// <summary>Verifies that calling write with a canceled cancellation token fails with
    /// <see cref="OperationCanceledException" />.</summary>
    [Test]
    public async Task Stream_write_with_canceled_token_fails()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        IMultiplexedStream clientStream = await clientConnection.CreateStreamAsync(
            bidirectional: true,
            default).ConfigureAwait(false);

        // Act
        ValueTask<FlushResult> task = clientStream.Output.WriteAsync(
            _oneBytePayload,
            new CancellationToken(canceled: true));

        // Assert
        Assert.CatchAsync<OperationCanceledException>(async () => await task);
    }

    /// <summary>Verifies that aborting writes correctly raises the abort exception.</summary>
    [Test]
    public async Task Stream_abort_cancels_write()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        IMultiplexedStream clientStream = await clientConnection.CreateStreamAsync(
            bidirectional: true,
            default).ConfigureAwait(false);

        // Fill the stream send buffer until WriteAsync blocks.
        var writeTask = new ValueTask<FlushResult>();
        byte[] payload = new byte[128 * 1024];
        while (writeTask.IsCompleted)
        {
            writeTask = clientStream.Output.WriteAsync(payload, CancellationToken.None);
            await Task.Delay(100); // Delay to ensure the write is really blocking.
        }

        // Act
        clientStream.Abort(new InvalidOperationException());

        // Act/Assert
        Assert.CatchAsync<InvalidOperationException>(async () => await writeTask);
    }

    [Test]
    public async Task Create_client_connection_with_unknown_server_address_parameter_fails_with_format_exception()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();

        var serverAddress = new ServerAddress(new Uri("icerpc://foo?unknown-parameter=foo"));

        // Act/Asserts
        Assert.Throws<FormatException>(() => clientTransport.CreateConnection(
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
        Assert.Throws<FormatException>(() => serverTransport.Listen(
            serverAddress,
            new MultiplexedConnectionOptions(),
            provider.GetService<SslServerAuthenticationOptions>()));
    }

    [Test]
    public async Task Connection_server_address_transport_property_is_set()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var transport = provider.GetRequiredService<IMultiplexedClientTransport>().Name;
        IMultiplexedConnection clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        IListener<IMultiplexedConnection> listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Act
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(clientConnection.ServerAddress.Transport, Is.EqualTo(transport));
            Assert.That(serverConnection.ServerAddress.Transport, Is.EqualTo(transport));
        });
    }

    [Test]
    public async Task Listener_server_address_transport_property_is_set()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var transport = provider.GetRequiredService<IMultiplexedClientTransport>().Name;
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        // Act/Assert
        Assert.That(listener.ServerAddress.Transport, Is.EqualTo(transport));
    }

    /// <summary>Verifies that create stream fails if called before connect.</summary>
    [Test]
    public async Task Create_stream_before_calling_connect_fails()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        await using IMultiplexedConnection sut = provider.GetRequiredService<IMultiplexedConnection>();

        // Act/Assert
        Assert.That(
            async () => await sut.CreateStreamAsync(bidirectional: true, default),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task Close_connection()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        IMultiplexedConnection clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        IListener<IMultiplexedConnection> listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        // Act/Assert
        Assert.Multiple(() =>
        {
            Assert.That(async () => await clientConnection.CloseAsync(
                applicationErrorCode: 0ul,
                CancellationToken.None), Throws.Nothing);
            Assert.That(async () => await serverConnection.CloseAsync(
                applicationErrorCode: 0ul,
                CancellationToken.None), Throws.Nothing);
        });
    }

    [Test]
    public async Task Close_connection_on_both_sides()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        IMultiplexedConnection clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        IListener<IMultiplexedConnection> listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        // Act
        Task clientCloseTask = clientConnection.CloseAsync(applicationErrorCode: 0ul, CancellationToken.None);
        Task serverCloseTask = serverConnection.CloseAsync(applicationErrorCode: 0ul, CancellationToken.None);

        // Assert
        Assert.Multiple(() =>
        {
            Assert.That(() => clientCloseTask, Throws.Nothing);
            Assert.That(() => serverCloseTask, Throws.Nothing);
        });
    }

    /// <summary>Verifies we can read the properties of a stream after completing its Input and Output.</summary>
    [Test]
    public async Task Stream_properties_readable_after_input_and_output_completed()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        var sut = await CreateAndAcceptStreamAsync(clientConnection, serverConnection);

        // Exchange byte
        _ = await sut.LocalStream.Output.WriteAsync(_oneBytePayload);
        _ = await sut.RemoteStream.Output.WriteAsync(_oneBytePayload);
        _ = await sut.LocalStream.Input.ReadAsync();
        _ = await sut.RemoteStream.Input.ReadAsync();

        // Act
        sut.LocalStream.Output.Complete();
        // With Quic, there is occasionally a small delay between the CompleteWrites in Complete and the completion of
        // the WritesClosed task.
        await sut.LocalStream.WritesClosed;

        sut.RemoteStream.Output.Complete();
        await sut.RemoteStream.WritesClosed;

        sut.LocalStream.Input.Complete();
        sut.RemoteStream.Input.Complete();

        // Assert
        Assert.That(sut.LocalStream.Id, Is.EqualTo(sut.RemoteStream.Id));

        Assert.That(sut.LocalStream.IsBidirectional, Is.True);
        Assert.That(sut.RemoteStream.IsBidirectional, Is.True);

        Assert.That(sut.LocalStream.IsRemote, Is.False);
        Assert.That(sut.RemoteStream.IsRemote, Is.True);

        Assert.That(sut.LocalStream.IsStarted, Is.True);
        Assert.That(sut.RemoteStream.IsStarted, Is.True);

        Assert.That(sut.LocalStream.WritesClosed.IsCompletedSuccessfully, Is.True);
        Assert.That(sut.RemoteStream.WritesClosed.IsCompletedSuccessfully, Is.True);

        // TODO: with Quic it's Faulted because we did not read the endStream before calling Complete, while with
        // Slic it completes successfully.

        // Assert.That(sut.LocalStream.ReadsClosed.IsCompleted, Is.True); // TODO: occasionally fails with Quic
        Assert.That(sut.RemoteStream.ReadsClosed.IsCompleted, Is.True);
    }

    [Test]
    [Ignore("See #1859")]
    public async Task Close_client_connection_before_connect_fails_with_transport_connection_closed_error()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        IMultiplexedConnection clientConnection = provider.GetRequiredService<IMultiplexedConnection>();

        // Act
        await clientConnection.CloseAsync(applicationErrorCode: 4ul, default);

        // Assert
        TransportException? exception = Assert.ThrowsAsync<TransportException>(
            async () => await clientConnection.ConnectAsync(default));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.ErrorCode, Is.EqualTo(TransportErrorCode.ConnectionClosed));
            Assert.That(exception!.ApplicationErrorCode, Is.EqualTo(4ul));
        });
    }

    [Test]
    [Ignore("See #1859")]
    public async Task Close_server_connection_before_connect_fails_with_transport_connection_closed_error()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        IMultiplexedConnection clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        IListener<IMultiplexedConnection> listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        IMultiplexedConnection? serverConnection = null;
        Task acceptTask = AcceptAndCloseAsync();

        // Act/Assert
        TransportException? exception = Assert.ThrowsAsync<TransportException>(
            async () => await clientConnection.ConnectAsync(default));
        Assert.Multiple(() =>
        {
            Assert.That(exception!.ErrorCode, Is.EqualTo(TransportErrorCode.ConnectionClosed));
            Assert.That(exception!.ApplicationErrorCode, Is.EqualTo(2ul));
        });

        // Cleanup
        await serverConnection!.DisposeAsync();

        async Task AcceptAndCloseAsync()
        {
            serverConnection = (await listener.AcceptAsync(default)).Connection;
            await serverConnection.CloseAsync(applicationErrorCode: 2ul, default);
        }
    }

    /// <summary>Creates the service collection used for multiplexed transport conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();

    private static async Task<(IMultiplexedStream LocalStream, IMultiplexedStream RemoteStream)> CreateAndAcceptStreamAsync(
        IMultiplexedConnection localConnection,
        IMultiplexedConnection remoteConnection,
        bool isBidirectional = true)
    {
        IMultiplexedStream localStream = await localConnection.CreateStreamAsync(
            bidirectional: isBidirectional,
            default).ConfigureAwait(false);
        _ = await localStream.Output.WriteAsync(_oneBytePayload);
        IMultiplexedStream remoteStream = await remoteConnection.AcceptStreamAsync(default);
        ReadResult readResult = await remoteStream.Input.ReadAsync();
        remoteStream.Input.AdvanceTo(readResult.Buffer.End);
        return (localStream, remoteStream);
    }

    private static async Task CompleteStreamsAsync(
        (IMultiplexedStream LocalStream, IMultiplexedStream RemoteStream) sut)
    {
        await CompleteStreamAsync(sut.LocalStream);
        await CompleteStreamAsync(sut.RemoteStream);
    }

    private static async Task CompleteStreamAsync(IMultiplexedStream stream)
    {
        if (stream.IsBidirectional)
        {
            await stream.Input.CompleteAsync();
            await stream.Output.CompleteAsync();
        }
        else if (stream.IsRemote)
        {
            await stream.Input.CompleteAsync();
        }
        else
        {
            await stream.Output.CompleteAsync();
        }
    }

    private static async Task CompleteStreamsAsync(IEnumerable<IMultiplexedStream> streams)
    {
        foreach (IMultiplexedStream stream in streams)
        {
            await CompleteStreamAsync(stream);
        }
    }

    private static async Task<IMultiplexedConnection> ConnectAndAcceptConnectionAsync(
        IListener<IMultiplexedConnection> listener,
        IMultiplexedConnection connection)
    {
        var connectTask = connection.ConnectAsync(default);
        var acceptTask = listener.AcceptAsync(default);
        if (connectTask.IsFaulted)
        {
            await connectTask;
        }
        if (acceptTask.IsFaulted)
        {
            await acceptTask;
        }
        var serverConnection = (await acceptTask).Connection;
        await serverConnection.ConnectAsync(default);
        await connectTask;
        return serverConnection;
    }
}

public static class MultiplexedTransportServiceCollectionExtensions
{
    public static IServiceCollection AddMultiplexedTransportTest(this IServiceCollection services) =>
        services.AddSingleton(provider =>
        {
            var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
            var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
            var connection = clientTransport.CreateConnection(
                listener.ServerAddress,
                provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                provider.GetService<SslClientAuthenticationOptions>());
            return connection;
        });
}
