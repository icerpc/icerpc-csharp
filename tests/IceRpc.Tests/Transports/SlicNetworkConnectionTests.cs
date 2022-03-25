// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Transports.Tests;

[Parallelizable(ParallelScope.All)]
[Timeout(30000)]
public sealed class SlicNetworkConnectionTests
{
    private static readonly ReadOnlyMemory<byte> _oneBytePayload = new(new byte[] { 0xFF });

    private static readonly ReadOnlyMemory<byte> _oneMbPayload = new(
        Enumerable.Range(0, 1024 * 1024).Select(i => (byte)(i % 256)).ToArray());

    [Test]
    public async Task Connect_to_remote_endpoint()
    {
        await using IListener<IMultiplexedNetworkConnection> listener = CreateListener();
        await using IMultiplexedNetworkConnection sut = CreateConnection(listener.Endpoint);

        Task<NetworkConnectionInformation> connectTask = sut.ConnectAsync(default);

        IMultiplexedNetworkConnection serverConnection = await listener.AcceptAsync();
        await serverConnection.ConnectAsync(default);
        Assert.That(async () => await connectTask, Throws.Nothing);
    }

    /// <summary>Verifies that the connection iddle timeout is set to the peer iddle timeout after connect.</summary>
    [Test]
    public async Task Connection_iddle_timeout()
    {
        await using IListener<IMultiplexedNetworkConnection> listener = CreateListener(
            tcpOptions: new TcpServerTransportOptions
            {
                IdleTimeout = TimeSpan.FromSeconds(15)
            });
        
        await using SlicNetworkConnection sut = CreateConnection(
            listener.Endpoint,
            tcpOtions: new TcpClientTransportOptions
            {
                IdleTimeout = TimeSpan.FromSeconds(30)
            });

        Task<NetworkConnectionInformation> connectTask = sut.ConnectAsync(default);

        IMultiplexedNetworkConnection serverConnection = await listener.AcceptAsync();
        await serverConnection.ConnectAsync(default);
        await connectTask;

        Assert.That(sut.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(15)));
    }

    [Test]
    public async Task Accept_stream()
    {
        await using IListener<IMultiplexedNetworkConnection> listener = CreateListener();
        await using IMultiplexedNetworkConnection clientConnection = CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection sut = await ConnectAsync(clientConnection, listener);

        IMultiplexedStream clientStream = clientConnection.CreateStream(true);
        await clientStream.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream serverStream = await sut.AcceptStreamAsync(default);

        Assert.That(serverStream.Id, Is.EqualTo(clientStream.Id));

        await CompleteStreamAsync(clientStream);
    }

    [Test]
    public async Task Accept_stream_cancelation()
    {
        await using IListener<IMultiplexedNetworkConnection> listener = CreateListener();
        await using IMultiplexedNetworkConnection clientConnection = CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection sut = await ConnectAsync(clientConnection, listener);

        using var cancelationSource = new CancellationTokenSource();
        ValueTask<IMultiplexedStream> acceptTask = sut.AcceptStreamAsync(cancelationSource.Token);

        cancelationSource.Cancel();

        Assert.That(async () => await acceptTask, Throws.TypeOf<OperationCanceledException>());
    }

    /// <summary>Verify that after a stream consumed all the send credit, writes calls on the stream start blocking.
    /// </summary>
    [Test]
    public async Task Stream_write_blocks_after_consuming_the_send_credit()
    {
        // Arrange
        await using IListener<IMultiplexedNetworkConnection> listener = CreateListener(
            options: new SlicServerTransportOptions
            {
                PauseWriterThreshold = 1024 * 1024,
            });
        await using IMultiplexedNetworkConnection sut = CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAsync(sut, listener);
        IMultiplexedStream stream = sut.CreateStream(bidirectional: true);
        FlushResult result = await stream.Output.WriteAsync(_oneMbPayload, default);

        // Previous write call consumed all send credit this call cannot succeed
        ValueTask<FlushResult> writeTask = stream.Output.WriteAsync(_oneMbPayload, default);

        // Assert
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(writeTask.IsCompleted, Is.False);
    }

    /// <summary>Verify that the writes calls of other streams on the same connection aren't affected by a stream
    /// that consumed all its send credit. We create two streams on the same connection, write data to stream1 until
    /// we consume all its credits and writes start to block, a second stream is created and we ensure that its writes
    /// can proceed unaffected.</summary>
    [Test]
    public async Task Stream_write_blocking_does_not_affect_concurrent_streams()
    {
        // Arrange
        await using IListener<IMultiplexedNetworkConnection> listener = CreateListener(
            options: new SlicServerTransportOptions
            {
                PauseWriterThreshold = 1024 * 1024,
            });
        await using IMultiplexedNetworkConnection sut = CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAsync(sut, listener);
        IMultiplexedStream stream1 = sut.CreateStream(bidirectional: true);
        IMultiplexedStream stream2 = sut.CreateStream(bidirectional: true);

        FlushResult result = await stream1.Output.WriteAsync(_oneMbPayload, default);
        ValueTask<FlushResult> writeTask = stream1.Output.WriteAsync(_oneMbPayload, default);

        // Act

        // stream1 consumed all its send credit, this shouldn't affect stream2
        result = await stream2.Output.WriteAsync(_oneMbPayload, default);

        // Assert
        var serverStream1 = await serverConnection.AcceptStreamAsync(default);
        var serverStream2 = await serverConnection.AcceptStreamAsync(default);
        Assert.That(stream2.Id, Is.EqualTo(serverStream2.Id));
        var readResult = await serverStream2.Input.ReadAtLeastAsync(1024 * 1024);
        Assert.That(readResult.IsCanceled, Is.False);
        serverStream2.Input.AdvanceTo(readResult.Buffer.End);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(writeTask.IsCompleted, Is.False);
    }

    [Test]
    public async Task Can_resume_write_after_reaching_the_resume_writer_threshold()
    {
        await using IListener<IMultiplexedNetworkConnection> listener = CreateListener(
            options: new SlicServerTransportOptions
            {
                PauseWriterThreshold = 1024 * 1024,
                ResumeWriterThreshold = 1024 * 512
            });
        await using IMultiplexedNetworkConnection sut = CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAsync(sut, listener);
        
        IMultiplexedStream stream = sut.CreateStream(bidirectional: true);

        FlushResult result = await stream.Output.WriteAsync(_oneMbPayload, default);
        IMultiplexedStream serverStream = await serverConnection.AcceptStreamAsync(default);
        ValueTask<FlushResult> writeTask = stream.Output.WriteAsync(_oneMbPayload, default);
        ReadResult readResult = await serverStream.Input.ReadAtLeastAsync(1024 * 1024, default);

        // Act
        serverStream.Input.AdvanceTo(readResult.Buffer.GetPosition(1024 * 1024));

        Assert.That(async () => await writeTask, Throws.Nothing);
    }

    [Test]
    public async Task Disposing_the_connection_aborts_the_streams()
    {
        await using IListener<IMultiplexedNetworkConnection> listener = CreateListener();
        await using IMultiplexedNetworkConnection sut = CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAsync(sut, listener);

        IMultiplexedStream clientStream = sut.CreateStream(true);
        await clientStream.Output.WriteAsync(_oneBytePayload);

        await sut.DisposeAsync();

        Assert.ThrowsAsync<MultiplexedStreamAbortedException>(async () => await clientStream.Input.ReadAsync());
    }

    [Test]
    public async Task Max_concurrent_streams(
        [Values(1, 1024)] int maxStreamCount, 
        [Values(true, false)] bool bidirectional)
    {
        // Arrange
        var slicServerOptions = new SlicServerTransportOptions();
        if (bidirectional)
        {
            slicServerOptions.BidirectionalStreamMaxCount = maxStreamCount;
        }
        else
        {
            slicServerOptions.UnidirectionalStreamMaxCount = maxStreamCount;
        }

        await using IListener<IMultiplexedNetworkConnection> listener = CreateListener(options: slicServerOptions);
        await using IMultiplexedNetworkConnection sut = CreateConnection(listener.Endpoint);

        Task<NetworkConnectionInformation> connectTask = sut.ConnectAsync(default);
        IMultiplexedNetworkConnection serverConnection = await listener.AcceptAsync();
        await serverConnection.ConnectAsync(default);
        await connectTask;

        List<IMultiplexedStream> streams = await CreateStreamsAsync(
            sut,
            maxStreamCount,
            bidirectional,
            _oneBytePayload);

        // Act
        IMultiplexedStream lastStream = sut.CreateStream(bidirectional);

        // Assert

        // The last stream cannot start as we have already rich the max concurrent streams
        Assert.That(async () =>
            {
                var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                await lastStream.Output.WriteAsync(_oneBytePayload, cancellationSource.Token);
            },
            Throws.TypeOf<OperationCanceledException>());

        await CompleteStreamsAsync(streams);
    }

    private static SlicNetworkConnection CreateConnection(
        Endpoint endpoint,
        SlicClientTransportOptions? options = null,
        TcpClientTransportOptions? tcpOtions = null)
    {
        options ??= new SlicClientTransportOptions();
        options.SimpleClientTransport ??= new TcpClientTransport(tcpOtions ?? new TcpClientTransportOptions());
        var transport = new SlicClientTransport(options);
        return (SlicNetworkConnection) transport.CreateConnection(endpoint, null, NullLogger.Instance);
    }

    private static async Task<IMultiplexedNetworkConnection> ConnectAsync(
        IMultiplexedNetworkConnection connection,
        IListener<IMultiplexedNetworkConnection> listener)
    {
        Task<NetworkConnectionInformation> connectTask = connection.ConnectAsync(default);
        IMultiplexedNetworkConnection peerConnection = await listener.AcceptAsync();
        await peerConnection.ConnectAsync(default);
        await connectTask;
        return peerConnection;
    }

    private static IListener<IMultiplexedNetworkConnection> CreateListener(
        Endpoint? endpoint = null,
        SlicServerTransportOptions? options = null,
        TcpServerTransportOptions? tcpOptions = null)
    {
        endpoint ??= Endpoint.FromString("icerpc://127.0.0.1:0/");
        options ??= new SlicServerTransportOptions();
        options.SimpleServerTransport ??= new TcpServerTransport(tcpOptions ?? new TcpServerTransportOptions());
        var transport = new SlicServerTransport(options);
        return transport.Listen(endpoint.Value, null, NullLogger.Instance);
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
                await stream.Output.WriteAsync(payload);
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

    private static async Task CompleteStreamsAsync(List<IMultiplexedStream> streams)
    {
        foreach(IMultiplexedStream stream in streams)
        {
            await CompleteStreamAsync(stream);
        }
    }
}
