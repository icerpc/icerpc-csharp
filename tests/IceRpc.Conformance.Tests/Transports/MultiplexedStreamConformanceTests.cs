// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Conformance.Tests;

/// <summary>Conformance tests for the multiplexed transports streams.</summary>
public abstract class MultiplexedStreamConformanceTests
{
    private static IEnumerable<TestCaseData> WriteData
    {
        get
        {
            // These tests assume that the default segment size of the stream output is 4096.

            byte[] bytes1K = new byte[1024];
            byte[] bytes2K = new byte[2048];
            byte[] bytes3K = new byte[3072];
            byte[] bytes8K = new byte[8192];

            yield return new TestCaseData(Array.Empty<byte>(), Array.Empty<byte[]>());
            yield return new TestCaseData(Array.Empty<byte>(), new byte[][] { bytes1K });
            yield return new TestCaseData(Array.Empty<byte>(), new byte[][] { bytes2K, bytes3K });

            yield return new TestCaseData(bytes1K, new byte[][] { bytes1K });
            yield return new TestCaseData(bytes2K, new byte[][] { bytes3K });
            yield return new TestCaseData(bytes8K, new byte[][] { bytes1K });
        }
    }

    private static readonly ReadOnlyMemory<byte> _oneBytePayload = new(new byte[] { 0xFF });

    /// <summary>Ensures that completing the stream output after writing data doesn't discard the data. A successful
    /// write doesn't imply that the data is actually sent by the underlying transport. The completion of the stream
    /// output should make sure that this data buffered by the underlying transport is not discarded.</summary>
    [Test]
    public async Task Complete_stream_output_after_write_does_not_discard_data()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        byte[] buffer = new byte[512 * 1024];

        // Act
        Task writeTask = WriteDataAsync();

        // Assert
        Assert.That(async () => await ReadDataAsync(), Is.EqualTo(buffer.Length));
        Assert.That(() => writeTask, Throws.Nothing);

        async Task<int> ReadDataAsync()
        {
            ReadResult readResult;
            int readLength = 0;
            do
            {
                readResult = await sut.Remote.Input.ReadAsync(default);
                readLength += (int)readResult.Buffer.Length;
                sut.Remote.Input.AdvanceTo(readResult.Buffer.End);
            }
            while (!readResult.IsCompleted);
            return readLength;
        }

        async Task WriteDataAsync()
        {
            // Send a large buffer to ensure the transport (eventually) buffers the sending of the data.
            await sut.Local.Output.WriteAsync(buffer);

            // Act
            sut.Local.Output.Complete();
        }
    }

    /// <summary>Verifies that completing a stream with unflushed bytes fails with
    /// <see cref="NotSupportedException" />.</summary>
    [Test]
    public async Task Complete_stream_with_unflushed_bytes_fails()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        IMultiplexedStream stream = await clientServerConnection.Client.CreateStreamAsync(bidirectional: true, default);

        Memory<byte> buffer = stream.Output.GetMemory();
        stream.Output.Advance(buffer.Length);

        Assert.That(() => stream.Output.Complete(), Throws.TypeOf<InvalidOperationException>());

        stream.Input.Complete();
    }

    /// <summary>Verifies that create stream fails if called before connect.</summary>
    [Test]
    public async Task Create_stream_before_calling_connect_fails()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        // Act/Assert
        Assert.That(
            async () => await sut.Client.CreateStreamAsync(bidirectional: true, default),
            Throws.TypeOf<InvalidOperationException>());
    }

    [TestCase(true, true, 1, 0u)]
    [TestCase(false, true, 1, 1u)]
    [TestCase(true, false, 1, 2u)]
    [TestCase(false, false, 1, 3u)]
    [TestCase(true, true, 4, 12u)]
    [TestCase(false, true, 4, 13u)]
    [TestCase(true, false, 4, 14u)]
    [TestCase(false, false, 4, 15u)]
    public async Task Created_stream_has_expected_id(
        bool isClientInitiated,
        bool isBidirectional,
        int streamCount,
        ulong expectedId)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        IMultiplexedConnection localConnection =
            isClientInitiated ? clientServerConnection.Client : clientServerConnection.Server;
        IMultiplexedConnection remoteConnection =
            isClientInitiated ? clientServerConnection.Server : clientServerConnection.Client;
        IMultiplexedStream? localStream = null;
        IMultiplexedStream? remoteStream = null;
        var localStreams = new List<IMultiplexedStream>();
        var remoteStreams = new List<IMultiplexedStream>();

        // Act
        for (int i = 0; i < streamCount; i++)
        {
            localStream = await localConnection.CreateStreamAsync(bidirectional: isBidirectional, default);
            localStreams.Add(localStream);
            await localStream.Output.WriteAsync(new byte[1]);
            remoteStream = await remoteConnection.AcceptStreamAsync(default);
            remoteStreams.Add(remoteStream);
        }

        // Assert
        Assert.That(localStream, Is.Not.Null);
        Assert.That(remoteStream, Is.Not.Null);
        Assert.That(localStream!.Id, Is.EqualTo(expectedId));
        Assert.That(remoteStream!.Id, Is.EqualTo(expectedId));

        // Cleanup
        foreach (IMultiplexedStream stream in localStreams)
        {
            if (isBidirectional)
            {
                stream.Input.Complete();
            }
            stream.Output.Complete();
        }

        foreach (IMultiplexedStream stream in remoteStreams)
        {

            stream.Input.Complete();
            if (isBidirectional)
            {
                stream.Output.Complete();
            }
        }
    }

    [TestCase(MultiplexedConnectionCloseError.NoError, IceRpcError.ConnectionClosedByPeer)]
    [TestCase(MultiplexedConnectionCloseError.Aborted, IceRpcError.ConnectionAborted)]
    [TestCase(MultiplexedConnectionCloseError.ServerBusy, IceRpcError.ServerBusy)]
    [TestCase((MultiplexedConnectionCloseError)255, IceRpcError.ConnectionAborted)]
    public async Task Pending_stream_read_fails_with_peer_close_error_code_on_connection_close(
        MultiplexedConnectionCloseError closeError,
        IceRpcError expectedIceRpcError)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);

        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        IMultiplexedStream clientStream = await clientServerConnection.Client.CreateStreamAsync(
            bidirectional: true,
            default);
        await clientStream.Output.WriteAsync(new byte[1]);
        IMultiplexedStream serverStream = await clientServerConnection.Server.AcceptStreamAsync(default);

        ValueTask<ReadResult> readTask = clientStream.Input.ReadAsync();

        // Act
        await clientServerConnection.Server.CloseAsync(closeError, default);

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await readTask);
        Assert.That(exception?.IceRpcError, Is.EqualTo(expectedIceRpcError));
    }

    [Test]
    public async Task Stream_abort_read()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        // Act
        sut.Remote.Input.Complete(new ArgumentException()); // can be any exception

        // Assert
        Assert.That(
            async () =>
            {
                while (true)
                {
                    FlushResult result = await sut.Local.Output.WriteAsync(new byte[1024]);
                    if (result.IsCompleted)
                    {
                        return;
                    }
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                }
            },
            Throws.Nothing);
    }

    [Test]
    public async Task Stream_abort_write()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        // Act
        sut.Local.Output.Complete(new OperationCanceledException()); // can be any exception
        // Wait for the peer to receive the StreamWritesClosed frame.
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await sut.Remote.Input.ReadAsync());
        Assert.That(
            exception?.IceRpcError,
            Is.EqualTo(IceRpcError.TruncatedData),
            $"The test failed with an unexpected IceRpcError {exception}");
    }

    /// <summary>Verifies that we can read and write concurrently to multiple streams.</summary>
    /// <param name="delay">Number of milliseconds to delay the read and write operation.</param>
    /// <param name="streamCount">The number of streams to create.</param>
    /// <param name="segments">The number of segments to write to each stream.</param>
    /// <param name="payloadSize">The payload size to write with each write call.</param>
    [Test]
    public async Task Stream_full_duplex_communication(
        [Values(0, 5)] int delay,
        [Values(1, 16)] int streamCount,
        [Values(1, 32)] int segments,
        [Values(1, 16 * 1024)] int payloadSize)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        var streams = new LocalAndRemoteMultiplexedStreams[streamCount];
        for (int i = 0; i < streamCount; ++i)
        {
            streams[i] = await clientServerConnection.CreateAndAcceptStreamAsync();
        }

        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);

        var writeTasks = new List<Task>();
        var readTasks = new List<Task<byte[]>>();

        // Act
        for (int i = 0; i < streamCount; ++i)
        {
            writeTasks.Add(WriteAsync(streams[i].Local, segments, payload));
            readTasks.Add(ReadAsync(streams[i].Remote));
            writeTasks.Add(WriteAsync(streams[i].Remote, segments, payload));
            readTasks.Add(ReadAsync(streams[i].Local));
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

        for (int i = 0; i < streamCount; ++i)
        {
            streams[i].Dispose();
        }

        // Read the all the data from the stream. Once we reached the end of the stream, the data is returned as byte
        // array.
        async Task<byte[]> ReadAsync(IMultiplexedStream stream)
        {
            ReadResult result = default;
            while (true)
            {
                result = await stream.Input.ReadAsync();

                if (delay > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delay));
                }

                if (result.IsCompleted)
                {
                    break;
                }
                else
                {
                    stream.Input.AdvanceTo(result.Buffer.Start, result.Buffer.End);
                }
            }

            byte[] buffer = result.Buffer.ToArray();
            stream.Input.AdvanceTo(result.Buffer.End);
            return buffer;
        }

        // Write data on the stream in multiple segments.
        async Task WriteAsync(IMultiplexedStream stream, int segments, ReadOnlyMemory<byte> payload)
        {
            for (int i = 0; i < segments; ++i)
            {
                if (delay > 0)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delay));
                }

                FlushResult flushResult = await stream.Output.WriteAsync(payload, default);
                Assert.That(flushResult.IsCompleted, Is.False);
            }
            stream.Output.Complete();
        }
    }

    [Test]
    public async Task Stream_local_writes_are_closed_when_remote_input_is_completed(
        [Values] bool isBidirectional,
        [Values] bool abort)
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync(isBidirectional);

        // Act
        sut.Remote.Input.Complete(abort ? new Exception() : null);

        // Assert
        Assert.That(async () => await sut.Local.WritesClosed, Throws.Nothing);
    }

    /// <summary>Ensures that the stream output extends ReadOnlySequencePipeWriter.</summary>
    [Test]
    public async Task Stream_output_is_a_readonly_sequence_pipe_writer()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync(bidirectional: false);

        // Act/Assert
        Assert.That(sut.Local.Output, Is.InstanceOf<ReadOnlySequencePipeWriter>());
    }

    /// <summary>Ensures that the stream output can report unflushed bytes.</summary>
    [Test]
    public async Task Stream_output_can_report_unflushed_bytes()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync(bidirectional: false);
        var data = new byte[] { 0x1, 0x2, 0x3 };
        sut.Local.Output.Write(data);

        // Act/Assert
        Assert.That(sut.Local.Output.CanGetUnflushedBytes, Is.True);
        Assert.That(sut.Local.Output.UnflushedBytes, Is.EqualTo(3));

        await sut.Local.Output.FlushAsync();
    }

    [Test]
    public async Task Stream_local_writes_are_closed_when_local_output_completed(
        [Values] bool isBidirectional,
        [Values] bool abort)
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync(bidirectional: isBidirectional);

        // Act
        sut.Local.Output.Complete(abort ? new Exception() : null);

        // Assert
        Assert.That(async () => await sut.Local.WritesClosed, Throws.Nothing);
    }

    /// <summary>Verifies we can read the properties of a stream after completing its Input and Output.</summary>
    [Test]
    public async Task Stream_properties_readable_after_input_and_output_completed()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        // Act
        sut.Local.Output.Complete();
        sut.Local.Input.Complete();
        await sut.Local.WritesClosed;

        sut.Remote.Output.Complete();
        sut.Remote.Input.Complete();
        await sut.Remote.WritesClosed;

        // Assert
        Assert.That(sut.Local.Id, Is.EqualTo(sut.Remote.Id));

        Assert.That(sut.Local.IsBidirectional, Is.True);
        Assert.That(sut.Remote.IsBidirectional, Is.True);

        Assert.That(sut.Local.IsRemote, Is.False);
        Assert.That(sut.Remote.IsRemote, Is.True);

        Assert.That(sut.Local.IsStarted, Is.True);
        Assert.That(sut.Remote.IsStarted, Is.True);
    }

    /// <summary>Verifies that stream read can be canceled.</summary>
    [Test]
    public async Task Stream_read_cancellation()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        using var cts = new CancellationTokenSource();
        ValueTask<ReadResult> readTask = sut.Local.Input.ReadAsync(cts.Token);

        // Act
        cts.Cancel();

        // Assert
        Assert.That(async () => await readTask, Throws.InstanceOf<OperationCanceledException>());
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
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        sut.Remote.Output.Complete();

        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);
        Task writeTask = WriteAsync(sut.Local, segments, payload);

        // Act
        Task<byte[]> readTask = ReadAsync(sut.Remote, payloadSize * segments);

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
            stream.Input.Complete();
            return buffer;
        }

        async Task WriteAsync(IMultiplexedStream stream, int segments, ReadOnlyMemory<byte> payload)
        {
            for (int i = 0; i < segments; ++i)
            {
                await stream.Output.WriteAsync(payload, default);
                await Task.Yield();
            }
            stream.Output.Complete();
        }
    }

    [Test]
    public async Task Stream_read_returns_canceled_read_result_after_cancel_pending_read()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        await sut.Remote.Output.WriteAsync(_oneBytePayload);

        // Act
        sut.Local.Input.CancelPendingRead();
        await Task.Delay(100); // Delay to ensure the data is ready to be read by the client stream.

        // Assert
        ReadResult readResult1 = await sut.Local.Input.ReadAsync();
        sut.Local.Input.AdvanceTo(readResult1.Buffer.Start);

        ReadResult readResult2 = await sut.Local.Input.ReadAsync();
        sut.Local.Input.AdvanceTo(readResult2.Buffer.Start);

        Assert.That(readResult1.IsCanceled, Is.True);
        Assert.That(readResult1.IsCompleted, Is.False);
        Assert.That(readResult2.IsCanceled, Is.False);
        Assert.That(readResult2.Buffer, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task Stream_read_returns_canceled_read_result_on_cancel_pending_read()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        // Act
        ValueTask<ReadResult> readTask = sut.Local.Input.ReadAsync();
        sut.Local.Input.CancelPendingRead();

        // Assert
        ReadResult readResult1 = await readTask;
        sut.Local.Input.AdvanceTo(readResult1.Buffer.Start);

        Assert.That(async () => await sut.Remote.Output.WriteAsync(_oneBytePayload), Throws.Nothing);

        ReadResult? readResult2 = null;
        try
        {
            readResult2 = await sut.Local.Input.ReadAsync();
        }
        catch (IceRpcException exception) when (exception.IceRpcError == IceRpcError.OperationAborted)
        {
            // acceptable behavior (and that's what Quic does)
            // we get OperationAborted because we locally "aborted" the stream by calling CancelPendingRead.
        }

        Assert.That(readResult1.IsCanceled, Is.True);
        Assert.That(readResult1.IsCompleted, Is.False);

        if (readResult2 is not null)
        {
            Assert.That(readResult2.Value.IsCanceled, Is.False);
            Assert.That(readResult2.Value.Buffer, Has.Length.EqualTo(1));
            sut.Local.Input.AdvanceTo(readResult2.Value.Buffer.Start);
        }
    }

    /// <summary>Verifies that calling read with a canceled cancellation token fails with
    /// <see cref="OperationCanceledException" />.</summary>
    [Test]
    public async Task Stream_read_with_canceled_token_fails()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        // Act/Assert
        Assert.That(
            async () => await sut.Local.Input.ReadAsync(new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task Stream_remote_read_returns_completed_result_when_local_output_is_completed()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        // Act
        sut.Local.Output.Complete();

        // Assert
        ReadResult readResult = await sut.Remote.Input.ReadAsync();
        Assert.That(readResult.IsCompleted, Is.True);
    }

    [Test]
    public async Task Stream_remote_write_returns_completed_flush_result_when_local_input_is_completed()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        // Act
        sut.Local.Input.Complete();

        // Assert
        await Task.Delay(TimeSpan.FromMilliseconds(50)); // give time to the StreamReadsClosed frame to reach Output
        FlushResult flushResult = await sut.Remote.Output.WriteAsync(new byte[1]);
        Assert.That(flushResult.IsCompleted, Is.True);
    }

    [Test]
    public async Task Stream_remote_flush_returns_completed_flush_result_when_local_input_is_completed()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        Memory<byte> _ = sut.Remote.Output.GetMemory();
        sut.Remote.Output.Advance(1);

        // Act
        sut.Local.Input.Complete();

        // Assert
        await Task.Delay(TimeSpan.FromMilliseconds(50)); // give time to the StreamReadsClosed frame to reach Output
        FlushResult flushResult = await sut.Remote.Output.FlushAsync();
        Assert.That(flushResult.IsCompleted, Is.True);
    }

    [Test]
    public async Task Stream_write_empty_buffer_is_noop()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        // Act
        await sut.Local.Output.WriteAsync(ReadOnlyMemory<byte>.Empty);

        // We read at least 2 (instead of a plain read) otherwise with Quic, readResult.IsCompleted is false because
        // we get IsCompleted=true only when a _second_ call reads 0 bytes from the underlying QuicStream.
        Task<ReadResult> task = sut.Remote.Input.ReadAtLeastAsync(2).AsTask();
        await ((ReadOnlySequencePipeWriter)sut.Local.Output)
            .WriteAsync(new ReadOnlySequence<byte>(_oneBytePayload), endStream: true, default);
        ReadResult readResult = await task;

        // Assert
        Assert.That(readResult.IsCompleted, Is.True);
        Assert.That(readResult.Buffer.Length, Is.EqualTo(1));
    }

    [TestCaseSource(nameof(WriteData))]
    public async Task Stream_write(byte[] bufferedData, byte[][] writeData)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();
        PipeWriter output = sut.Local.Output;
        PipeReader input = sut.Remote.Input;
        if (bufferedData.Length > 0)
        {
            output.Write(bufferedData);
        }

        var pipe = new Pipe(new PipeOptions(minimumSegmentSize: 4096));
        foreach (ReadOnlyMemory<byte> buffer in writeData)
        {
            pipe.Writer.Write(buffer.Span);
        }
        pipe.Writer.Complete();
        _ = pipe.Reader.TryRead(out ReadResult dataReadResult);

        // Act
        _ = await ((ReadOnlySequencePipeWriter)output).WriteAsync(
            dataReadResult.Buffer,
            endStream: false,
            CancellationToken.None);
        output.Complete();

        // Assert
        ReadResult readResult;
        long length = 0;
        do
        {
            readResult = await input.ReadAsync();
            length += readResult.Buffer.Length;
            input.AdvanceTo(readResult.Buffer.End);
        }
        while (!readResult.IsCompleted);
        Assert.That(length, Is.EqualTo(bufferedData.Length + dataReadResult.Buffer.Length));

        pipe.Reader.Complete();
    }

    /// <summary>Verifies that calling write with a canceled cancellation token fails with
    /// <see cref="OperationCanceledException" />.</summary>
    [Test]
    public async Task Stream_write_with_canceled_token_fails()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();
        using var sut = await clientServerConnection.CreateAndAcceptStreamAsync();

        // Act
        ValueTask<FlushResult> task = sut.Local.Output.WriteAsync(
            _oneBytePayload,
            new CancellationToken(canceled: true));

        // Assert
        Assert.That(async () => await task, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Creates the service collection used for multiplexed listener conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();
}
