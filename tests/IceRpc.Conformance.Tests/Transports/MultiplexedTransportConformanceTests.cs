// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Transports.Tests;

/// <summary>Conformance tests for the multiplexed transports.</summary>
[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public abstract class MultiplexedTransportConformanceTests
{
    private static readonly ReadOnlyMemory<byte> _oneBytePayload = new(new byte[] { 0xFF });

    [Test]
    public async Task Accept_a_stream([Values(true, false)] bool serverInitiated)
    {
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection = transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAndAcceptAsync(clientConnection, listener);

        IMultiplexedStream localStream =
            (serverInitiated ? serverConnection : clientConnection).CreateStream(true);
        await localStream.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream remoteStream =
            await (serverInitiated ? clientConnection : serverConnection).AcceptStreamAsync(default);

        Assert.That(localStream.Id, Is.EqualTo(remoteStream.Id));

        await CompleteStreamAsync(remoteStream);
        await CompleteStreamAsync(localStream);
    }

    [Test]
    public async Task Accessing_stream_id_before_starting_the_stream_fails(
        [Values(true, false)] bool bidirectional)
    {
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection sut = transportProvider.CreateConnection(listener.Endpoint);

        IMultiplexedStream clientStream = sut.CreateStream(bidirectional);

        Assert.Throws<InvalidOperationException>(() => _ = clientStream.Id); // stream is not started
    }

    [Test]
    public async Task Cancel_accept_stream()
    {
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection =
            transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection sut = await ConnectAndAcceptAsync(clientConnection, listener);
        using var cancellationSource = new CancellationTokenSource();
        ValueTask<IMultiplexedStream> acceptTask = sut.AcceptStreamAsync(cancellationSource.Token);

        cancellationSource.Cancel();

        Assert.That(async () => await acceptTask, Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public async Task Complete_stream_with_unflushed_bytes_fails()
    {
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection sut = transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAndAcceptAsync(sut, listener);
        IMultiplexedStream stream = sut.CreateStream(bidirectional: true);

        Memory<byte> buffer = stream.Output.GetMemory();
        stream.Output.Advance(buffer.Length);

        Assert.That(async () => await stream.Output.CompleteAsync(), Throws.TypeOf<NotSupportedException>());

        await stream.Input.CompleteAsync();
    }

    /// <summary>Creates the test fixture that provides the multiplexed transport to test with.</summary>
    public abstract IMultiplexedTransportProvider CreateMultiplexedTransportProvider();

    [Test]
    public async Task Disposing_the_connection_aborts_the_streams(
        [Values(true, false)] bool disposeServerConnection)
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection = transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAndAcceptAsync(clientConnection, listener);
        IMultiplexedStream clientStream = clientConnection.CreateStream(true);
        await clientStream.Output.WriteAsync(_oneBytePayload);
        IMultiplexedStream serverStream = await serverConnection.AcceptStreamAsync(default);

        IMultiplexedNetworkConnection diposedConnection = disposeServerConnection ? serverConnection : clientConnection;
        IMultiplexedStream diposedStream = disposeServerConnection ? serverStream : clientStream;
        IMultiplexedStream peerStream = disposeServerConnection ? clientStream : serverStream;

        // Act
        await diposedConnection.DisposeAsync();

        // Assert

        // The streams of the disposed connection get ObjectDisposedException and the streams of the peer connection get
        // ConnectionLostException.
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await diposedStream.Input.ReadAsync());
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await diposedStream.Output.WriteAsync(_oneBytePayload));

        Assert.ThrowsAsync<ConnectionLostException>(async () => await peerStream.Input.ReadAsync());
        Assert.ThrowsAsync<ConnectionLostException>(async () => await peerStream.Output.WriteAsync(_oneBytePayload));

        await CompleteStreamAsync(clientStream);
        await CompleteStreamAsync(serverStream);
    }

    [Test]
    public async Task Cannot_create_streams_with_a_disposed_connection(
        [Values(true, false)] bool disposeServerConnection)
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection = transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAndAcceptAsync(clientConnection, listener);


        IMultiplexedNetworkConnection diposedConnection = disposeServerConnection ? serverConnection : clientConnection;
        IMultiplexedNetworkConnection peerConnection = disposeServerConnection ? serverConnection : clientConnection;
        // Act
        await diposedConnection.DisposeAsync();

        // Assert

        // The streams of the disposed connection get ObjectDisposedException and the streams of the peer connection get
        // ConnectionLostException.
        var disposedStream = diposedConnection.CreateStream(true);
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await disposedStream.Output.WriteAsync(_oneBytePayload));

        var peerStream = peerConnection.CreateStream(true);
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await peerStream.Output.WriteAsync(_oneBytePayload));
    }

    [Test]
    public async Task Peer_does_not_accept_more_than_max_concurrent_streams(
        [Values(1, 1024)] int maxStreamCount,
        [Values(true, false)] bool bidirectional)
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener(
            bidirectionalStreamMaxCount: bidirectional ? maxStreamCount : null,
            unidirectionalStreamMaxCount: bidirectional ? null : maxStreamCount);
        await using IMultiplexedNetworkConnection sut = transportProvider.CreateConnection(listener.Endpoint);

        await using IMultiplexedNetworkConnection serverConnection = await ConnectAndAcceptAsync(sut, listener);

        List<IMultiplexedStream> streams = await CreateStreamsAsync(
            sut,
            maxStreamCount,
            bidirectional,
            _oneBytePayload);

        // Act
        IMultiplexedStream lastStream = sut.CreateStream(bidirectional);

        // Assert
        Assert.That(
            async () =>
            {
                var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                await lastStream.Output.WriteAsync(_oneBytePayload, cancellationSource.Token);
            },
            Throws.TypeOf<OperationCanceledException>());
        await CompleteStreamsAsync(streams);
    }

    [Test]
    public async Task Max_bidirectional_stream_stress_test(
        [Values(32, 128)] int maxStreamCount,
        [Values(32, 64)] int segments,
        [Values(32 * 1024, 64 * 1024)] int payloadSize)
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener(
            bidirectionalStreamMaxCount: maxStreamCount);
        await using IMultiplexedNetworkConnection clientConnection = transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAndAcceptAsync(clientConnection, listener);

        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);

        int streamCount = 0;
        int streamCountMax = 0;
        var mutex = new object();

        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < maxStreamCount * 10; ++i)
        {
            tasks.Add(CreateStreamAsync(clientConnection));
        }

        for (int i = 0; i < maxStreamCount * 10; ++i)
        {
            tasks.Add(AcceptStreamAsync(await serverConnection.AcceptStreamAsync(default)));
        }

        // Assert
        await Task.WhenAll(tasks);
        Assert.That(streamCountMax, Is.EqualTo(maxStreamCount));

        async Task CreateStreamAsync(IMultiplexedNetworkConnection connection)
        {
            IMultiplexedStream stream = connection.CreateStream(true);
            await stream.Output.WriteAsync(_oneBytePayload);

            lock (mutex)
            {
                streamCount++;
                streamCountMax = Math.Max(streamCount, streamCountMax);
            }

            await Task.WhenAll(
                WriteDataAsync(stream, segments, payload),
                ReadDataAsync(stream, payload.Length * segments));

            lock (mutex)
            {
                streamCount--;
            }
            await stream.Output.CompleteAsync();
            await stream.Input.CompleteAsync();
        }

        async Task AcceptStreamAsync(IMultiplexedStream stream)
        {
            ReadResult readResult = await stream.Input.ReadAsync();
            stream.Input.AdvanceTo(readResult.Buffer.GetPosition(1));

            await Task.WhenAll(
                WriteDataAsync(stream, segments, payload),
                ReadDataAsync(stream, payload.Length * segments));

            await stream.Output.CompleteAsync();
            await stream.Input.CompleteAsync();
        }

        async Task WriteDataAsync(
            IMultiplexedStream stream,
            int segments,
            ReadOnlyMemory<byte> payload)
        {
            for (int i = 0; i < segments; ++i)
            {
                await stream.Output.WriteAsync(payload);
            }
        }

        async Task ReadDataAsync(IMultiplexedStream stream, long size)
        {
            while (size > 0)
            {
                ReadResult readResult = await stream.Input.ReadAsync();
                size -= readResult.Buffer.Length;
                stream.Input.AdvanceTo(readResult.Buffer.End);
            }
        }
    }

    [Test]
    public async Task After_reach_max_stream_count_completing_a_stream_allows_accepting_a_new_one(
       [Values(1, 1024)] int maxStreamCount,
       [Values(true, false)] bool bidirectional)
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener(
            bidirectionalStreamMaxCount: bidirectional ? maxStreamCount : null,
            unidirectionalStreamMaxCount: bidirectional ? null : maxStreamCount);
        await using IMultiplexedNetworkConnection sut = transportProvider.CreateConnection(listener.Endpoint);

        await using IMultiplexedNetworkConnection serverConnection = await ConnectAndAcceptAsync(sut, listener);

        List<IMultiplexedStream> streams = await CreateStreamsAsync(
            sut,
            maxStreamCount,
            bidirectional,
            _oneBytePayload);
        IMultiplexedStream lastStream = sut.CreateStream(bidirectional);
        ValueTask<FlushResult> writeTask = lastStream.Output.WriteAsync(_oneBytePayload, default);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        IMultiplexedStream serverStream = await serverConnection.AcceptStreamAsync(default);
        if (bidirectional)
        {
            await serverStream.Output.CompleteAsync();
        }
        bool isCompleted = writeTask.IsCompleted;

        // Act
        await serverStream.Input.CompleteAsync();

        // Assert
        Assert.That(isCompleted, Is.False);
        Assert.That(async () => await writeTask, Throws.Nothing);
        await CompleteStreamsAsync(streams);
    }

    [TestCase(100)]
    [TestCase(15)]
    public async Task Stream_abort_read(byte errorCode)
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection =
            transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection =
            await ConnectAndAcceptAsync(clientConnection, listener);

        IMultiplexedStream clientStream = clientConnection.CreateStream(bidirectional: true);
        _ = await clientStream.Output.WriteAsync(_oneBytePayload);
        IMultiplexedStream serverStream = await serverConnection.AcceptStreamAsync(default);

        // Act
        await serverStream.Input.CompleteAsync(new MultiplexedStreamAbortedException(error: errorCode));

        // Assert
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        MultiplexedStreamAbortedException ex = Assert.CatchAsync<MultiplexedStreamAbortedException>(
            async () => await clientStream.Output.WriteAsync(_oneBytePayload));
        Assert.That(ex.ErrorCode, Is.EqualTo(errorCode));

         // Complete the pipe readers/writers to shutdown the stream.
        await clientStream.Output.CompleteAsync();

        await clientStream.Input.CompleteAsync();
        await serverStream.Output.CompleteAsync();
    }

    [TestCase(100)]
    [TestCase(15)]
    public async Task Stream_abort_write(byte errorCode)
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection =
            transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection =
            await ConnectAndAcceptAsync(clientConnection, listener);

        IMultiplexedStream clientStream = clientConnection.CreateStream(bidirectional: true);
        _ = await clientStream.Output.WriteAsync(_oneBytePayload, default);
        IMultiplexedStream serverStream = await serverConnection.AcceptStreamAsync(default);

        // Act
        await clientStream.Output.CompleteAsync(new MultiplexedStreamAbortedException(error: errorCode));

        // Assert
        // Wait for the peer to receive the StreamStopSending/StreamReset frame.
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        MultiplexedStreamAbortedException ex = Assert.CatchAsync<MultiplexedStreamAbortedException>(
            async () => await serverStream.Input.ReadAsync());
        Assert.That(ex.ErrorCode, Is.EqualTo(errorCode));

        // Complete the pipe readers/writers to shutdown the stream.
        await serverStream.Input.CompleteAsync();

        await clientStream.Input.CompleteAsync();
        await serverStream.Output.CompleteAsync();
    }

    /// <summary>Verifies that we can read and write concurrently to multiple streams.</summary>
    /// <param name="readDelay">Number of milliseconds to delay in the read operation.</param>
    /// <param name="writeDelay">Number of milliseconds to delay in the write operation.</param>
    /// <param name="streams">The number of streams to create.</param>
    /// <param name="segments">The number of segment to write to each stream.</param>
    /// <param name="payloadSize">The payload size to write with each write call.</param>
    [Test]
    public async Task Stream_full_duplex_communication(
        [Values(0, 20)] int readDelay,
        [Values(0, 20)] int writeDelay,
        [Values(1, 4, 8, 16, 32)] int streams,
        [Values(1, 16, 32, 64)] int segments,
        [Values(1, 256, 16 * 1024)] int payloadSize)
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection =
            transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAndAcceptAsync(clientConnection, listener);

        var clientStreams = new IMultiplexedStream[streams];
        var serverStreams = new IMultiplexedStream[streams];

        for (int i = 0; i < streams; ++i)
        {
            IMultiplexedStream clientStream = clientConnection.CreateStream(bidirectional: true);
            clientStreams[i] = clientStream;
            _ = await clientStream.Output.WriteAsync(_oneBytePayload, default);
            IMultiplexedStream serverStream = await serverConnection.AcceptStreamAsync(default);
            serverStreams[i] = serverStream;
            ReadResult readResult = await serverStream.Input.ReadAsync();
            serverStream.Input.AdvanceTo(readResult.Buffer.End);
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

        foreach(Task<byte[]> readTask in readTasks)
        {
            var readResult = new ArraySegment<byte>(await readTask);
            for (int i = 0; i < segments; ++i)
            {
                Assert.That(
                    Enumerable.SequenceEqual(
                        readResult.Slice(i * payload.Length, payload.Length),
                        payloadData),
                    Is.True);
            }
        }

        async Task<byte[]> ReadAsync(IMultiplexedStream stream, long size)
        {
            byte[] buffer = new byte[size];
            var segment = new ArraySegment<byte>(buffer);
            while (segment.Count > 0)
            {
                if (readDelay > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(readDelay));
                }
                ReadResult readResult = await stream.Input.ReadAsync();
                foreach (ReadOnlyMemory<byte> src in readResult.Buffer)
                {
                    src.CopyTo(segment);
                    segment = segment.Slice(src.Length);
                }
                stream.Input.AdvanceTo(readResult.Buffer.End);
            }
            await stream.Input.CompleteAsync();
            return buffer;
        }

        async Task WriteAsync(IMultiplexedStream stream, int segments, ReadOnlyMemory<byte> payload)
        {
            for (int i = 0; i < segments; ++i)
            {
                if (writeDelay > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(writeDelay));
                }
                await stream.Output.WriteAsync(payload, default);
            }
            await stream.Output.CompleteAsync();
        }
    }

    [Test]
    public async Task Stream_read_examine_data_without_consuming(
        [Values(64, 128, 256)] int segments,
        [Values(32 * 1024, 64 * 1024)] int payloadSize)
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection =
            transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAndAcceptAsync(clientConnection, listener);

        IMultiplexedStream clientStream = clientConnection.CreateStream(bidirectional: true);
        await clientStream.Input.CompleteAsync();
        _ = await clientStream.Output.WriteAsync(_oneBytePayload, default);
        IMultiplexedStream serverStream = await serverConnection.AcceptStreamAsync(default);
        serverStream.Input.AdvanceTo((await serverStream.Input.ReadAsync()).Buffer.End);
        await serverStream.Output.CompleteAsync();

        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);

        // Act
        var writeTask = WriteAsync(clientStream, segments, payload);
        var readTask = ReadAsync(serverStream, payloadSize * segments);

        // Assert
        await Task.WhenAll(writeTask, readTask);

        var readResult = new ArraySegment<byte>(await readTask);
        for (int i = 0; i < segments; ++i)
        {
            Assert.That(
                Enumerable.SequenceEqual(
                    readResult.Slice(i * payload.Length, payload.Length),
                    payloadData),
                Is.True);
        }

        async Task<byte[]> ReadAsync(IMultiplexedStream stream, long size)
        {
            ReadResult readResult = default;
            do
            {
                readResult = await stream.Input.ReadAsync();
                stream.Input.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);
            }
            while (readResult.Buffer.Length < size);
            byte[] buffer = new byte[size];
            var segment = new ArraySegment<byte>(buffer);
            foreach(ReadOnlyMemory<byte> src in readResult.Buffer)
            {
                src.CopyTo(segment);
                segment = segment.Slice(src.Length);
            }
            await stream.Input.CompleteAsync();
            return buffer;
        }

        async Task WriteAsync(IMultiplexedStream stream, int segments, ReadOnlyMemory<byte> payload)
        {
            for (int i = 0; i < segments; ++i)
            {
                await stream.Output.WriteAsync(payload, default);
            }
            await stream.Output.CompleteAsync();
        }
    }

    [Test]
    public async Task Stream_output_completes_after_completing_peer_input(
        [Values(32, 64 * 1024, 1024 * 1024)] int payloadSize)
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection =
            transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAndAcceptAsync(clientConnection, listener);

        IMultiplexedStream clientStream = clientConnection.CreateStream(bidirectional: true);
        await clientStream.Input.CompleteAsync();
        _ = await clientStream.Output.WriteAsync(_oneBytePayload, default);
        IMultiplexedStream serverStream = await serverConnection.AcceptStreamAsync(default);
        serverStream.Input.AdvanceTo((await serverStream.Input.ReadAsync()).Buffer.End);
        await serverStream.Output.CompleteAsync();

        Task writeTask = WriteAsync(clientStream);
        ReadResult readResult = await serverStream.Input.ReadAtLeastAsync(payloadSize * 2);
        serverStream.Input.AdvanceTo(readResult.Buffer.End);

        // Act
        await serverStream.Input.CompleteAsync();

        // Assert
        Assert.That(async () => await writeTask, Throws.Nothing);

        async Task WriteAsync(IMultiplexedStream stream)
        {
            var payload = new ReadOnlyMemory<byte>(
                Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray());
            FlushResult flushResult = default;
            while (!flushResult.IsCompleted)
            {
                using var cancelationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
                try
                {
                    flushResult = await stream.Output.WriteAsync(payload, cancelationSource.Token);
                }
                catch (OperationCanceledException)
                {
                }
            }
            await stream.Output.CompleteAsync();
        }
    }

    [Test]
    public async Task Stream_read_with_canceled_token()
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection =
            transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection =
            await ConnectAndAcceptAsync(clientConnection, listener);

        IMultiplexedStream clientStream = clientConnection.CreateStream(bidirectional: true);

        // Act/Assert
        Assert.CatchAsync<OperationCanceledException>(
            async () => await clientStream.Input.ReadAsync(new CancellationToken(canceled: true)));
    }

    [Test]
    public async Task Stream_read_canceled()
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection =
            transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection =
            await ConnectAndAcceptAsync(clientConnection, listener);

        IMultiplexedStream clientStream = clientConnection.CreateStream(bidirectional: true);
        using var cancelationSource = new CancellationTokenSource();
        ValueTask<ReadResult> readTask = clientStream.Input.ReadAsync(cancelationSource.Token);

        // Act
        cancelationSource.Cancel();

        // Assert
        Assert.CatchAsync<OperationCanceledException>(async () => await readTask);
    }

    [Test]
    public async Task Stream_write_with_canceled_token()
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection =
            transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection =
            await ConnectAndAcceptAsync(clientConnection, listener);

        IMultiplexedStream clientStream = clientConnection.CreateStream(bidirectional: true);

        // Act/Assert
        Assert.CatchAsync<OperationCanceledException>(
            async () => await clientStream.Output.WriteAsync(_oneBytePayload, new CancellationToken(canceled: true)));
    }

    [Test]
    public async Task Write_to_a_stream_before_calling_connect_fails()
    {
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection sut = transportProvider.CreateConnection(listener.Endpoint);
        IMultiplexedStream stream = sut.CreateStream(bidirectional: true);

        Assert.That(
            async () => await stream.Output.WriteAsync(_oneBytePayload, default),
            Throws.TypeOf<InvalidOperationException>());
    }

    private static async Task<IMultiplexedNetworkConnection> ConnectAndAcceptAsync(
        IMultiplexedNetworkConnection connection,
        IListener<IMultiplexedNetworkConnection> listener)
    {
        Task<NetworkConnectionInformation> connectTask = connection.ConnectAsync(default);
        IMultiplexedNetworkConnection peerConnection = await listener.AcceptAsync();
        await peerConnection.ConnectAsync(default);
        await connectTask;
        return peerConnection;
    }

    private static async Task<List<IMultiplexedStream>> CreateStreamsAsync(
        IMultiplexedNetworkConnection connection,
        int count,
        bool bidirectional,
        ReadOnlyMemory<byte> payload)
    {
        var streams = new List<IMultiplexedStream>();
        for (int i = 0; i < count; i++)
        {
            IMultiplexedStream stream = connection.CreateStream(bidirectional);
            streams.Add(stream);
            if (!payload.IsEmpty)
            {
                await stream.Output.WriteAsync(payload, default);
            }
        }
        return streams;
    }

    private static async Task CompleteStreamAsync(IMultiplexedStream stream)
    {
        if (stream.IsBidirectional)
        {
            await stream.Input.CompleteAsync();
        }
        await stream.Output.CompleteAsync();
    }

    private static async Task CompleteStreamsAsync(IEnumerable<IMultiplexedStream> streams)
    {
        foreach(IMultiplexedStream stream in streams)
        {
            await CompleteStreamAsync(stream);
        }
    }

    public interface IMultiplexedTransportProvider
    {
        /// <summary>Creates a listener using the underlying multiplexed server transport.</summary>
        /// <param name="endpoint">The listener endpoint</param>
        /// <returns>The listener.</returns>
        IListener<IMultiplexedNetworkConnection> CreateListener(
            Endpoint? endpoint = null,
            int? bidirectionalStreamMaxCount = null,
            int? unidirectionalStreamMaxCount = null);

        /// <summary>Creates a connection using the underlying multiplexed client transport.</summary>
        /// <param name="endpoint">The connection endpoint.</param>
        /// <returns>The connection.</returns>
        IMultiplexedNetworkConnection CreateConnection(Endpoint endpoint);
    }

    public class SlicMultiplexedTransporttransportProvider : IMultiplexedTransportProvider
    {
        private readonly SlicServerTransportOptions? _slicServerOptions;
        private readonly SlicClientTransportOptions? _slicClientOptions;

        public SlicMultiplexedTransporttransportProvider(
            SlicServerTransportOptions serverTransportOptions,
            SlicClientTransportOptions clientTransportOptions)
        {
            _slicServerOptions = serverTransportOptions;
            _slicClientOptions = clientTransportOptions;
        }

        public IMultiplexedNetworkConnection CreateConnection(Endpoint endpoint)
        {
            SlicClientTransportOptions options = _slicClientOptions ?? new SlicClientTransportOptions();
            var transport = new SlicClientTransport(options);
            return transport.CreateConnection(endpoint, null, NullLogger.Instance);
        }

        public IListener<IMultiplexedNetworkConnection> CreateListener(
            Endpoint? endpoint = null,
            int? bidirectionalStreamMaxCount = null,
            int? unidirectionalStreamMaxCount = null)
        {
            SlicServerTransportOptions options = _slicServerOptions ?? new SlicServerTransportOptions();

            if (bidirectionalStreamMaxCount != null)
            {
                options.BidirectionalStreamMaxCount = bidirectionalStreamMaxCount.Value;
            }

            if (unidirectionalStreamMaxCount != null)
            {
                options.UnidirectionalStreamMaxCount = unidirectionalStreamMaxCount.Value;
            }

            options.SimpleServerTransport ??= new TcpServerTransport();
            var transport = new SlicServerTransport(options);
            return transport.Listen(
                 endpoint ?? Endpoint.FromString($"icerpc://{Guid.NewGuid()}/"),
                 null,
                 NullLogger.Instance);
        }
    }
}

[Timeout(35000)]
[Parallelizable(ParallelScope.All)]
public class SlicConformanceTests : MultiplexedTransportConformanceTests
{
    /// <summary>The multiplexed transports for conformance testing.</summary>
    public override IMultiplexedTransportProvider CreateMultiplexedTransportProvider()
    {
        var coloc = new ColocTransport();
        return new SlicMultiplexedTransporttransportProvider(
            new SlicServerTransportOptions
            {
                SimpleServerTransport = coloc.ServerTransport
            },
            new SlicClientTransportOptions
            {
                SimpleClientTransport = coloc.ClientTransport
            });
    }
}
