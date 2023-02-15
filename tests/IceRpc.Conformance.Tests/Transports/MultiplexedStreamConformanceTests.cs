// Copyright (c) ZeroC, Inc.

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
    private static readonly ReadOnlyMemory<byte> _oneBytePayload = new(new byte[] { 0xFF });

    /// <summary>Ensures that completing the stream output after writing data doesn't discard the data. A successful
    /// write doesn't imply that the data is actually sent by the underlying transport. The completion of the stream
    /// output should make sure that this data buffered by the underlying transport is not discarded.</summary>
    [Test]
    public async Task Complete_stream_output_after_write_does_not_discard_data()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server);

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
                readResult = await sut.RemoteStream.Input.ReadAsync(default);
                readLength += (int)readResult.Buffer.Length;
                sut.RemoteStream.Input.AdvanceTo(readResult.Buffer.End);
            }
            while (!readResult.IsCompleted);
            return readLength;
        }

        async Task WriteDataAsync()
        {
            // Send a large buffer to ensure the transport (eventually) buffers the sending of the data.
            await sut.LocalStream.Output.WriteAsync(buffer);

            // Act
            sut.LocalStream.Output.Complete();
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
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        // Act/Assert
        Assert.That(
            async () => await sut.Client.CreateStreamAsync(bidirectional: true, default),
            Throws.TypeOf<InvalidOperationException>());
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
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);

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
        Assert.That(
            async () => await readTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(expectedIceRpcError));
    }

    [Test]
    public async Task Stream_abort_read()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server);

        // Act
        sut.RemoteStream.Input.Complete(new ArgumentException()); // can be any exception

        // Assert
        Assert.That(
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
            },
            Throws.Nothing);
    }

    [Test]
    public async Task Stream_abort_write()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server);

        // Act
        sut.LocalStream.Output.Complete(new OperationCanceledException()); // can be any exception
        // Wait for the peer to receive the Reset frame.
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        // Assert
        Assert.That(
            async () => await sut.RemoteStream.Input.ReadAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.TruncatedData));
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
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        var streams = new LocalAndRemoteStreams[streamCount];
        for (int i = 0; i < streamCount; ++i)
        {
            streams[i] = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
                clientServerConnection.Client,
                clientServerConnection.Server);
        }

        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);

        var writeTasks = new List<Task>();
        var readTasks = new List<Task<byte[]>>();

        // Act
        for (int i = 0; i < streamCount; ++i)
        {
            writeTasks.Add(WriteAsync(streams[i].LocalStream, segments, payload));
            readTasks.Add(ReadAsync(streams[i].RemoteStream));
            writeTasks.Add(WriteAsync(streams[i].RemoteStream, segments, payload));
            readTasks.Add(ReadAsync(streams[i].LocalStream));
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
        [Values(false, true)] bool isBidirectional,
        [Values(false, true)] bool abort)
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server,
            isBidirectional);

        // Act
        sut.RemoteStream.Input.Complete(abort ? new Exception() : null);

        // Assert
        Assert.That(async () => await sut.LocalStream.WritesClosed, Throws.Nothing);
    }

    [Test]
    public async Task Stream_local_writes_are_closed_when_local_output_completed(
        [Values(false, true)] bool isBidirectional,
        [Values(false, true)] bool abort)
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server,
            isBidirectional);

        // Act
        sut.LocalStream.Output.Complete(abort ? new Exception() : null);

        // Assert
        Assert.That(async () => await sut.LocalStream.WritesClosed, Throws.Nothing);
    }

    [Test]
    public async Task Stream_local_reads_are_closed_when_remote_output_is_completed([Values(false, true)] bool abort)
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server,
            true);

        // Act
        sut.RemoteStream.Output.Complete(abort ? new Exception() : null);

        // Assert
        if (!abort)
        {
            // The stream read side only completes once EOS is consumed.
            ReadResult readResult = await sut.LocalStream.Input.ReadAsync();
            Assert.That(readResult.IsCompleted, Is.True);
        }
        Assert.That(async () => await sut.LocalStream.ReadsClosed, Throws.Nothing);
    }

    [Test]
    public async Task Stream_local_reads_are_closed_when_remote_sends_end_of_stream([Values] bool emptyPayload)
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server,
            true);

        // Act
        await ((ReadOnlySequencePipeWriter)sut.RemoteStream.Output).WriteAsync(
            emptyPayload ? ReadOnlySequence<byte>.Empty : new ReadOnlySequence<byte>(_oneBytePayload),
            endStream: true,
            CancellationToken.None);;

        // Assert

        // Reads aren't closed before the data is read.
        ReadResult readResult = await sut.LocalStream.Input.ReadAsync();
        Assert.That(async () => await sut.LocalStream.ReadsClosed, Throws.Nothing);

        // Make sure the data can be consumed from the reader even when reads are closed.
        Assert.That(readResult.Buffer.Length, Is.EqualTo(emptyPayload ? 0 : 1));
        sut.LocalStream.Input.AdvanceTo(readResult.Buffer.End);
        if (!readResult.IsCompleted)
        {
            // A multiplexed transport implementation might not necessarily report the reader completion with the last
            // piece of data sent even if the data and the EOS were sent at the same time (it's the case of Quic).
            readResult = await sut.LocalStream.Input.ReadAsync();
        }
        Assert.That(readResult.IsCompleted, Is.True);
    }

    [Test]
    public async Task Stream_local_reads_are_closed_when_local_input_completed([Values(false, true)] bool abort)
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server,
            true);

        // Act
        sut.LocalStream.Input.Complete(abort ? new Exception() : null);

        // Assert
        Assert.That(async () => await sut.LocalStream.ReadsClosed, Throws.Nothing);
    }

    /// <summary>Verifies we can read the properties of a stream after completing its Input and Output.</summary>
    [Test]
    public async Task Stream_properties_readable_after_input_and_output_completed()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server);

        // Act
        sut.LocalStream.Output.Complete();
        sut.LocalStream.Input.Complete();
        await Task.WhenAll(sut.LocalStream.ReadsClosed, sut.LocalStream.WritesClosed);

        sut.RemoteStream.Output.Complete();
        sut.RemoteStream.Input.Complete();
        await Task.WhenAll(sut.RemoteStream.ReadsClosed, sut.RemoteStream.WritesClosed);

        // Assert
        Assert.That(sut.LocalStream.Id, Is.EqualTo(sut.RemoteStream.Id));

        Assert.That(sut.LocalStream.IsBidirectional, Is.True);
        Assert.That(sut.RemoteStream.IsBidirectional, Is.True);

        Assert.That(sut.LocalStream.IsRemote, Is.False);
        Assert.That(sut.RemoteStream.IsRemote, Is.True);

        Assert.That(sut.LocalStream.IsStarted, Is.True);
        Assert.That(sut.RemoteStream.IsStarted, Is.True);
    }

    /// <summary>Verifies that stream read can be canceled.</summary>
    [Test]
    public async Task Stream_read_cancellation()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        IMultiplexedStream clientStream = await clientServerConnection.Client.CreateStreamAsync(
            bidirectional: true,
            default).ConfigureAwait(false);
        using var cts = new CancellationTokenSource();
        ValueTask<ReadResult> readTask = clientStream.Input.ReadAsync(cts.Token);

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
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server);
        sut.RemoteStream.Output.Complete();

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
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server);

        await sut.RemoteStream.Output.WriteAsync(_oneBytePayload);

        // Act
        sut.LocalStream.Input.CancelPendingRead();
        await Task.Delay(100); // Delay to ensure the data is ready to be read by the client stream.

        // Assert
        ReadResult readResult1 = await sut.LocalStream.Input.ReadAsync();
        ReadResult readResult2 = await sut.LocalStream.Input.ReadAsync();

        Assert.That(readResult1.IsCanceled, Is.True);
        Assert.That(readResult1.IsCompleted, Is.False);
        Assert.That(readResult2.IsCanceled, Is.False);
        Assert.That(readResult2.Buffer, Has.Length.EqualTo(1));
    }

    [Test]
    public async Task Stream_read_returns_canceled_read_result_on_cancel_pending_read()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server);

        // Act
        ValueTask<ReadResult> readTask = sut.LocalStream.Input.ReadAsync();
        sut.LocalStream.Input.CancelPendingRead();

        // Assert
        ReadResult readResult1 = await readTask;

        Assert.That(async () => await sut.RemoteStream.Output.WriteAsync(_oneBytePayload), Throws.Nothing);

        ReadResult? readResult2 = null;
        try
        {
            readResult2 = await sut.LocalStream.Input.ReadAsync();
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
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        IMultiplexedStream clientStream = await clientServerConnection.Client.CreateStreamAsync(
            bidirectional: true,
            default).ConfigureAwait(false);

        // Act/Assert
        Assert.That(
            async () => await clientStream.Input.ReadAsync(new CancellationToken(canceled: true)),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task Stream_remote_read_returns_completed_result_when_local_output_is_completed()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server);

        // Act
        sut.LocalStream.Output.Complete();

        // Assert
        ReadResult readResult = await sut.RemoteStream.Input.ReadAsync();
        Assert.That(readResult.IsCompleted, Is.True);
    }

    [Test]
    public async Task Stream_remote_write_returns_completed_flush_result_when_local_input_is_completed()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server);

        // Act
        sut.LocalStream.Input.Complete();

        // Assert
        await Task.Delay(TimeSpan.FromMilliseconds(50)); // give time to StopSending frame to reach Output
        FlushResult flushResult = await sut.RemoteStream.Output.WriteAsync(new byte[1]);
        Assert.That(flushResult.IsCompleted, Is.True);
    }

    [Test]
    public async Task Stream_remote_flush_returns_completed_flush_result_when_local_input_is_completed()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server);
        Memory<byte> _ = sut.RemoteStream.Output.GetMemory();
        sut.RemoteStream.Output.Advance(1);

        // Act
        sut.LocalStream.Input.Complete();

        // Assert
        await Task.Delay(TimeSpan.FromMilliseconds(50)); // give time to StopSending frame to reach Output
        FlushResult flushResult = await sut.RemoteStream.Output.FlushAsync();
        Assert.That(flushResult.IsCompleted, Is.True);
    }

    [Test]
    public async Task Stream_write_empty_buffer_is_noop()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        using var sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientServerConnection.Client,
            clientServerConnection.Server);

        // Act
        await sut.LocalStream.Output.WriteAsync(ReadOnlyMemory<byte>.Empty);

        // We read at least 2 (instead of a plain read) otherwise with Quic, readResult.IsCompleted is false because
        // we get IsCompleted=true only when a _second_ call reads 0 bytes from the underlying QuicStream.
        Task<ReadResult> task = sut.RemoteStream.Input.ReadAtLeastAsync(2).AsTask();
        await ((ReadOnlySequencePipeWriter)sut.LocalStream.Output)
            .WriteAsync(new ReadOnlySequence<byte>(_oneBytePayload), endStream: true, default);
        ReadResult readResult = await task;

        // Assert
        Assert.That(readResult.IsCompleted, Is.True);
        Assert.That(readResult.Buffer.Length, Is.EqualTo(1));
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
        var clientServerConnection = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await clientServerConnection.AcceptAndConnectAsync();

        IMultiplexedStream clientStream = await clientServerConnection.Client.CreateStreamAsync(
            bidirectional: true,
            default).ConfigureAwait(false);

        // Act
        ValueTask<FlushResult> task = clientStream.Output.WriteAsync(
            _oneBytePayload,
            new CancellationToken(canceled: true));

        // Assert
        Assert.That(async () => await task,Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Creates the service collection used for multiplexed listener conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();
}
