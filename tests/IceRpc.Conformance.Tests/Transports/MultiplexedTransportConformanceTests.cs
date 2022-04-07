// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
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
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);

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
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection sut = provider.CreateConnection();

        IMultiplexedStream clientStream = sut.CreateStream(bidirectional);

        Assert.Throws<InvalidOperationException>(() => _ = clientStream.Id); // stream is not started
    }

    [Test]
    public async Task Cancel_accept_stream()
    {
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection sut = provider.CreateConnection();
        using var cancellationSource = new CancellationTokenSource();
        ValueTask<IMultiplexedStream> acceptTask = sut.AcceptStreamAsync(cancellationSource.Token);

        cancellationSource.Cancel();

        Assert.That(async () => await acceptTask, Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public async Task Complete_stream_with_unflushed_bytes_fails()
    {
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);
        IMultiplexedStream stream = clientConnection.CreateStream(bidirectional: true);

        Memory<byte> buffer = stream.Output.GetMemory();
        stream.Output.Advance(buffer.Length);

        Assert.That(async () => await stream.Output.CompleteAsync(), Throws.TypeOf<NotSupportedException>());

        await stream.Input.CompleteAsync();
    }

    /// <summary>Creates the test fixture that provides the multiplexed transport to test with.</summary>
    public abstract ServiceCollection CreateServiceCollection();

    [Test]
    public async Task Disposing_the_connection_aborts_the_streams(
        [Values(true, false)] bool disposeServerConnection)
    {
        // Arrange
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);
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
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);


        IMultiplexedNetworkConnection diposedConnection = disposeServerConnection ? serverConnection : clientConnection;
        IMultiplexedNetworkConnection peerConnection = disposeServerConnection ? serverConnection : clientConnection;
        // Act
        await diposedConnection.DisposeAsync();

        // Assert

        // The streams of the disposed connection get ObjectDisposedException and the streams of the peer connection get
        // ConnectionLostException.
        var disposedStream = diposedConnection.CreateStream(true);
        Assert.ThrowsAsync<ObjectDisposedException>(
            async () => await disposedStream.Output.WriteAsync(_oneBytePayload));

        var peerStream = peerConnection.CreateStream(true);
        Assert.ThrowsAsync<ObjectDisposedException>(async () => await peerStream.Output.WriteAsync(_oneBytePayload));
    }

    [Test]
    public async Task Peer_does_not_accept_more_than_max_concurrent_streams(
        [Values(1, 1024)] int maxStreamCount,
        [Values(true, false)] bool bidirectional)
    {
        // Arrange
        var multiplexedOptions = new MultiplexedTransportOptions();
        if (bidirectional)
        {
            multiplexedOptions.BidirectionalStreamMaxCount = maxStreamCount;
        }
        else
        {
            multiplexedOptions.UnidirectionalStreamMaxCount = maxStreamCount;
        }
        await using var provider = CreateServiceCollection().AddScoped(_ => multiplexedOptions).BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);

        List<IMultiplexedStream> streams = await CreateStreamsAsync(
            clientConnection,
            maxStreamCount,
            bidirectional,
            _oneBytePayload);

        // Act
        IMultiplexedStream lastStream = clientConnection.CreateStream(bidirectional);

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
        [Values(16, 32)] int maxStreamCount,
        [Values(32, 64, 128)] int maxStreamFactor)
    {
        // Arrange
        var multiplexedOptions = new MultiplexedTransportOptions { BidirectionalStreamMaxCount = maxStreamCount };

        await using var provider = CreateServiceCollection().AddScoped(_ => multiplexedOptions).BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);

        const int payloadSize = 1024;
        const int segments = 8;

        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);

        int streamCount = 0;
        int streamCountMax = 0;
        var mutex = new object();

        var streams = new List<IMultiplexedStream>();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < maxStreamCount * maxStreamFactor; ++i)
        {
            var stream = clientConnection.CreateStream(true);
            tasks.Add(ClientReadWriteAsync(stream));
            streams.Add(stream);
        }

        for (int i = 0; i < maxStreamCount * maxStreamFactor; ++i)
        {
            tasks.Add(ServerReadWriteAsync(await serverConnection.AcceptStreamAsync(default)));
        }

        // Assert
        await Task.WhenAll(tasks);
        Assert.That(streamCountMax, Is.EqualTo(maxStreamCount));

        await CompleteStreamsAsync(streams);

        async Task ClientReadWriteAsync(IMultiplexedStream stream)
        {
            await stream.Output.WriteAsync(_oneBytePayload);

            await Task.WhenAll(
                WriteDataAsync(stream, segments, payload),
                ReadDataAsync(stream, payload.Length * segments));
        }

        async Task ServerReadWriteAsync(IMultiplexedStream stream)
        {
            lock (mutex)
            {
                streamCount++;
                streamCountMax = Math.Max(streamCount, streamCountMax);
            }

            ReadResult readResult = await stream.Input.ReadAsync();
            stream.Input.AdvanceTo(readResult.Buffer.GetPosition(1));

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
    public async Task Max_unidirectional_stream_stress_test(
        [Values(16, 32)] int maxStreamCount,
        [Values(32, 64, 128)] int maxStreamFactor)
    {
        // Arrange
        var multiplexedOptions = new MultiplexedTransportOptions { UnidirectionalStreamMaxCount = maxStreamCount };

        await using var provider = CreateServiceCollection().AddScoped(_ => multiplexedOptions).BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);

        const int payloadSize = 1024;
        const int segments = 8;

        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);

        int streamCount = 0;
        int streamCountMax = 0;
        var mutex = new object();

        var streams = new List<IMultiplexedStream>();
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < maxStreamCount * maxStreamFactor; ++i)
        {
            var stream = clientConnection.CreateStream(bidirectional: false);
            tasks.Add(ClientWriteAsync(stream));
            streams.Add(stream);
        }

        for (int i = 0; i < maxStreamCount * maxStreamFactor; ++i)
        {
            tasks.Add(ServerReadAsync(await serverConnection.AcceptStreamAsync(default)));
        }

        // Assert
        await Task.WhenAll(tasks);
        Assert.That(streamCountMax, Is.EqualTo(maxStreamCount));

        await CompleteStreamsAsync(streams);

        async Task ClientWriteAsync(IMultiplexedStream stream)
        {
            await stream.Output.WriteAsync(_oneBytePayload);

            await WriteDataAsync(stream, segments, payload);
        }

        async Task ServerReadAsync(IMultiplexedStream stream)
        {
            lock (mutex)
            {
                streamCount++;
                streamCountMax = Math.Max(streamCount, streamCountMax);
            }

            ReadResult readResult = await stream.Input.ReadAsync();
            stream.Input.AdvanceTo(readResult.Buffer.GetPosition(1));

            await ReadDataAsync(stream, payload.Length * segments);

            lock (mutex)
            {
                streamCount--;
            }
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
        var multiplexedOptions = new MultiplexedTransportOptions();
        if (bidirectional)
        {
            multiplexedOptions.BidirectionalStreamMaxCount = maxStreamCount;
        }
        else
        {
            multiplexedOptions.UnidirectionalStreamMaxCount = maxStreamCount;
        }
        await using var provider = CreateServiceCollection().AddScoped(_ => multiplexedOptions).BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);

        List<IMultiplexedStream> streams = await CreateStreamsAsync(
            clientConnection,
            maxStreamCount,
            bidirectional,
            _oneBytePayload);
        IMultiplexedStream lastStream = clientConnection.CreateStream(bidirectional);
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
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);

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
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);

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
        [Values(1, 8, 16, 32)] int streams,
        [Values(1, 32, 64)] int segments,
        [Values(1, 16 * 1024)] int payloadSize)
    {
        // Arrange
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);

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
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);

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
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);

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
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);

        IMultiplexedStream clientStream = clientConnection.CreateStream(bidirectional: true);

        // Act/Assert
        Assert.CatchAsync<OperationCanceledException>(
            async () => await clientStream.Input.ReadAsync(new CancellationToken(canceled: true)));
    }

    [Test]
    public async Task Stream_read_canceled()
    {
        // Arrange
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);

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
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection clientConnection = provider.CreateConnection();
        await using IMultiplexedNetworkConnection serverConnection =
            await provider.AcceptConnectionAsync(clientConnection);

        IMultiplexedStream clientStream = clientConnection.CreateStream(bidirectional: true);

        // Act/Assert
        Assert.CatchAsync<OperationCanceledException>(
            async () => await clientStream.Output.WriteAsync(_oneBytePayload, new CancellationToken(canceled: true)));
    }

    [Test]
    public async Task Write_to_a_stream_before_calling_connect_fails()
    {
        await using var provider = CreateServiceCollection().BuildServiceProvider();
        await using IMultiplexedNetworkConnection sut = provider.CreateConnection();

        IMultiplexedStream stream = sut.CreateStream(bidirectional: true);

        Assert.That(
            async () => await stream.Output.WriteAsync(_oneBytePayload, default),
            Throws.TypeOf<InvalidOperationException>());
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
}

[Timeout(35000)]
[Parallelizable(ParallelScope.All)]
public class SlicConformanceTests : MultiplexedTransportConformanceTests
{
    /// <summary>Creates the service collection used for Slic multiplexed transports for conformance testing.</summary>
    public override ServiceCollection CreateServiceCollection() => new SlicServiceCollection();
}

public class SlicServiceCollection : ServiceCollection
{
    public SlicServiceCollection()
    {
        this.AddScoped(_ => new ColocTransport());

        this.AddScoped<IServerTransport<IMultiplexedNetworkConnection>>(provider =>
        {
            var colocTransport = provider.GetRequiredService<ColocTransport>();
            var serverOptions = new SlicServerTransportOptions();
            var multiplexedTransportOptions = provider.GetRequiredService<MultiplexedTransportOptions>();
            if (multiplexedTransportOptions != null)
            {
                serverOptions.BidirectionalStreamMaxCount = multiplexedTransportOptions.BidirectionalStreamMaxCount;
                serverOptions.UnidirectionalStreamMaxCount = multiplexedTransportOptions.UnidirectionalStreamMaxCount;
            }
            serverOptions.SimpleServerTransport = colocTransport.ServerTransport;
            return new SlicServerTransport(serverOptions);
        });

        this.AddScoped<IClientTransport<IMultiplexedNetworkConnection>>(provider =>
        {
            var colocTransport = provider.GetRequiredService<ColocTransport>();
            var clientOptions = new SlicClientTransportOptions();
            var multiplexedTransportOptions = provider.GetRequiredService<MultiplexedTransportOptions>();
            if (multiplexedTransportOptions != null)
            {
                clientOptions.BidirectionalStreamMaxCount = multiplexedTransportOptions.BidirectionalStreamMaxCount;
                clientOptions.UnidirectionalStreamMaxCount = multiplexedTransportOptions.UnidirectionalStreamMaxCount;
            }
            clientOptions.SimpleClientTransport = colocTransport.ClientTransport;
            return new SlicClientTransport(clientOptions);
        });

        this.AddScoped(provider =>
        {
            var serverTransport = provider.GetRequiredService<IServerTransport<IMultiplexedNetworkConnection>>();
            return serverTransport.Listen(
                Endpoint.FromString($"icerpc://{Guid.NewGuid()}/"),
                null,
                NullLogger.Instance);
        });

        this.AddScoped(_ => new MultiplexedTransportOptions());
    }
}

public static class SlicServiceProviderExtensions
{
    public static IMultiplexedNetworkConnection CreateConnection(this IServiceProvider provider)
    {
        var listener = provider.GetRequiredService<IListener<IMultiplexedNetworkConnection>>();
        var clientTransport = provider.GetRequiredService<IClientTransport<IMultiplexedNetworkConnection>>();
        var connection = clientTransport.CreateConnection(listener.Endpoint, null, NullLogger.Instance);
        return connection;
    }

    public static async Task<IMultiplexedNetworkConnection> AcceptConnectionAsync(
        this IServiceProvider provider, IMultiplexedNetworkConnection connection)
    {
        var listener = provider.GetRequiredService<IListener<IMultiplexedNetworkConnection>>();
        var connectTask = connection.ConnectAsync(default);
        var serverConnection = await listener.AcceptAsync();
        await serverConnection.ConnectAsync(default);
        await connectTask;
        return serverConnection;
    }
}
