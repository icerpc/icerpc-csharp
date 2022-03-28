// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Transports.Tests;

/// <summary>Conformance tests for the multiplexed transports.</summary>
[Timeout(30000)]
[Parallelizable(ParallelScope.All)]
public abstract class MultiplexedTransportConformanceTests
{
    private static readonly ReadOnlyMemory<byte> _oneBytePayload = new(new byte[] { 0xFF });

    private static readonly ReadOnlyMemory<byte> _oneMbPayload = new(
        Enumerable.Range(0, 1024 * 1024).Select(i => (byte)(i % 256)).ToArray());

    [Test]
    public async Task Accept_stream_from_client_to_server()
    {
        IMultiplexedTransportTestFixture testFixture = CreateMultiplexedTransportTestFixture();
        await using IListener<IMultiplexedNetworkConnection> listener = testFixture.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAsync(clientConnection, listener);
        IMultiplexedStream serverStream = serverConnection.CreateStream(true);
        await serverStream.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream clientStream = await clientConnection.AcceptStreamAsync(default);

        Assert.That(serverStream.Id, Is.EqualTo(clientStream.Id));
    }

    [Test]
    public async Task Accept_stream_from_server_to_client()
    {
        IMultiplexedTransportTestFixture testFixture = CreateMultiplexedTransportTestFixture();
        await using IListener<IMultiplexedNetworkConnection> listener = testFixture.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection sut = await ConnectAsync(clientConnection, listener);
        IMultiplexedStream clientStream = clientConnection.CreateStream(true);
        await clientStream.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream serverStream = await sut.AcceptStreamAsync(default);

        Assert.That(serverStream.Id, Is.EqualTo(clientStream.Id));
    }

    [Test]
    public async Task Accept_stream_canceled()
    {
        IMultiplexedTransportTestFixture testFixture = CreateMultiplexedTransportTestFixture();
        await using IListener<IMultiplexedNetworkConnection> listener = testFixture.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection sut = await ConnectAsync(clientConnection, listener);
        using var cancellationSource = new CancellationTokenSource();
        ValueTask<IMultiplexedStream> acceptTask = sut.AcceptStreamAsync(cancellationSource.Token);

        cancellationSource.Cancel();

        Assert.That(async () => await acceptTask, Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public async Task Complete_stream_with_unflused_bytes_fails()
    {
        IMultiplexedTransportTestFixture testFixture = CreateMultiplexedTransportTestFixture();
        await using IListener<IMultiplexedNetworkConnection> listener = testFixture.CreateListener(
            pauseWriterThreshold: 1024 * 1024);
        await using IMultiplexedNetworkConnection sut = testFixture.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAsync(sut, listener);
        IMultiplexedStream stream = sut.CreateStream(bidirectional: true);

        Memory<byte> buffer = stream.Output.GetMemory();
        stream.Output.Advance(buffer.Length);

        Assert.That(async () => await stream.Output.CompleteAsync(), Throws.TypeOf<NotSupportedException>());
    }

    /// <summary>Creates the test fixture that provides the multiplexed transport to test with.</summary>
    public abstract IMultiplexedTransportTestFixture CreateMultiplexedTransportTestFixture();

    [Test]
    public async Task Disposing_the_connection_aborts_the_streams()
    {
        IMultiplexedTransportTestFixture testFixture = CreateMultiplexedTransportTestFixture();
        await using IListener<IMultiplexedNetworkConnection> listener = testFixture.CreateListener();
        await using IMultiplexedNetworkConnection sut = testFixture.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAsync(sut, listener);
        IMultiplexedStream clientStream = sut.CreateStream(true);
        await clientStream.Output.WriteAsync(_oneBytePayload);

        await sut.DisposeAsync();

        Assert.ThrowsAsync<MultiplexedStreamAbortedException>(async () => await clientStream.Input.ReadAsync());
    }

    [Test]
    public async Task Peer_does_not_accept_more_than_max_concurrent_streams(
        [Values(1, 1024)] int maxStreamCount,
        [Values(true, false)] bool bidirectional)
    {
        // Arrange
        IMultiplexedTransportTestFixture testFixture = CreateMultiplexedTransportTestFixture();
        await using IListener<IMultiplexedNetworkConnection> listener = testFixture.CreateListener(
            bidirectionalStreamMaxCount: bidirectional ? maxStreamCount : null,
            unidirectionalStreamMaxCount: bidirectional ? null : maxStreamCount);
        await using IMultiplexedNetworkConnection sut = testFixture.CreateConnection(listener.Endpoint);

        await using IMultiplexedNetworkConnection serverConnection = await ConnectAsync(sut, listener);

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
    public async Task Stream_full_duplex_communication(
        [Values(1, 16, 32, 64)] int segments,
        [Values(1, 256, 1024 * 1024)] int payloadSize)
    {
        // Arrange
        IMultiplexedTransportTestFixture testFixture = CreateMultiplexedTransportTestFixture();
        await using IListener<IMultiplexedNetworkConnection> listener = testFixture.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection = testFixture.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAsync(clientConnection, listener);

        IMultiplexedStream clientStream = clientConnection.CreateStream(bidirectional: true);
        FlushResult result = await clientStream.Output.WriteAsync(_oneBytePayload, default);
        IMultiplexedStream serverStream = await serverConnection.AcceptStreamAsync(default);

        var payload = new ReadOnlyMemory<byte>(
            Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray());

        // Act
        Task clientWriteTask = WriteAsync(clientStream);
        Task serverReadTask = ReadAsync(serverStream);
        Task serverWriteTask = WriteAsync(serverStream);
        Task clientReadTask = ReadAsync(clientStream);

        // Assert
        await Task.WhenAll(clientWriteTask, serverWriteTask, clientReadTask, serverReadTask);

        async Task ReadAsync(IMultiplexedStream stream)
        {
            long size = payload.Length * segments;
            while (size > 0)
            {
                ReadResult readResult = await stream.Input.ReadAsync();
                size -= readResult.Buffer.Length;
                stream.Input.AdvanceTo(readResult.Buffer.End, readResult.Buffer.End);
            }
            await stream.Input.CompleteAsync();
        }

        async Task WriteAsync(IMultiplexedStream stream)
        {
            for (int i = 0; i < segments; ++i)
            {
                await stream.Output.WriteAsync(payload, default);
            }
            await stream.Output.CompleteAsync();
        }
    }

    [Test]
    public async Task Stream_write_blocks_after_consuming_the_send_credit()
    {
        IMultiplexedTransportTestFixture testFixture = CreateMultiplexedTransportTestFixture();
        await using IListener<IMultiplexedNetworkConnection> listener = testFixture.CreateListener(
            pauseWriterThreshold: 1024 * 1024);
        await using IMultiplexedNetworkConnection sut = testFixture.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAsync(sut, listener);
        IMultiplexedStream stream = sut.CreateStream(bidirectional: true);
        FlushResult result = await stream.Output.WriteAsync(_oneMbPayload, default);

        ValueTask<FlushResult> writeTask = stream.Output.WriteAsync(_oneMbPayload, default);

        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(writeTask.IsCompleted, Is.False);
    }

    [Test]
    public async Task Stream_write_blocking_does_not_affect_concurrent_streams()
    {
        // Arrange
        IMultiplexedTransportTestFixture testFixture = CreateMultiplexedTransportTestFixture();
        await using IListener<IMultiplexedNetworkConnection> listener = testFixture.CreateListener(
            pauseWriterThreshold: 1024 * 1024);
        await using IMultiplexedNetworkConnection sut = testFixture.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAsync(sut, listener);
        IMultiplexedStream stream1 = sut.CreateStream(bidirectional: true);
        IMultiplexedStream stream2 = sut.CreateStream(bidirectional: true);

        FlushResult result = await stream1.Output.WriteAsync(_oneMbPayload, default);
        ValueTask<FlushResult> writeTask = stream1.Output.WriteAsync(_oneMbPayload, default);

        // Act

        // stream1 consumed all its send credit, this shouldn't affect stream2
        result = await stream2.Output.WriteAsync(_oneMbPayload, default);

        // Assert
        IMultiplexedStream serverStream1 = await serverConnection.AcceptStreamAsync(default);
        IMultiplexedStream serverStream2 = await serverConnection.AcceptStreamAsync(default);
        Assert.That(stream2.Id, Is.EqualTo(serverStream2.Id));
        ReadResult readResult = await serverStream2.Input.ReadAtLeastAsync(1024 * 1024);
        Assert.That(readResult.IsCanceled, Is.False);
        serverStream2.Input.AdvanceTo(readResult.Buffer.End);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(writeTask.IsCompleted, Is.False);
    }

    [Test]
    public async Task Write_resumes_after_reaching_the_resume_writer_threshold()
    {
        // Arrange
        IMultiplexedTransportTestFixture testFixture = CreateMultiplexedTransportTestFixture();
        await using IListener<IMultiplexedNetworkConnection> listener = testFixture.CreateListener(
            pauseWriterThreshold: 1024 * 1024,
            resumeWriterThreshold: 1024 * 512);
        await using IMultiplexedNetworkConnection sut = testFixture.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAsync(sut, listener);

        IMultiplexedStream stream = sut.CreateStream(bidirectional: true);

        FlushResult result = await stream.Output.WriteAsync(_oneMbPayload, default);
        IMultiplexedStream serverStream = await serverConnection.AcceptStreamAsync(default);
        ValueTask<FlushResult> writeTask = stream.Output.WriteAsync(_oneMbPayload, default);
        ReadResult readResult = await serverStream.Input.ReadAtLeastAsync(1536 * 1024, default);

        // Act
        serverStream.Input.AdvanceTo(readResult.Buffer.GetPosition(1536 * 1024));

        // Assert
        Assert.That(async () => await writeTask, Throws.Nothing);
    }

    [Test]
    public async Task Write_to_a_stream_before_calling_connect_fails()
    {
        IMultiplexedTransportTestFixture testFixture = CreateMultiplexedTransportTestFixture();
        await using IListener<IMultiplexedNetworkConnection> listener = testFixture.CreateListener();
        await using IMultiplexedNetworkConnection sut = testFixture.CreateConnection(listener.Endpoint);
        IMultiplexedStream stream = sut.CreateStream(bidirectional: true);

        Assert.That(
            async () => await stream.Output.WriteAsync(_oneBytePayload, default),
            Throws.TypeOf<InvalidOperationException>());
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

    private static async Task CompleteStreamsAsync(IEnumerable<IMultiplexedStream> streams)
    {
        foreach(IMultiplexedStream stream in streams)
        {
            await CompleteStreamAsync(stream);
        }
    }

    public interface IMultiplexedTransportTestFixture
    {
        /// <summary>Creates a listener using the underlying multiplexed server transport.</summary>
        /// <param name="endpoint">The listener endpoint</param>
        /// <returns>The listener.</returns>
        IListener<IMultiplexedNetworkConnection> CreateListener(
            Endpoint? endpoint = null,
            int? pauseWriterThreshold = null,
            int? resumeWriterThreshold = null,
            int? bidirectionalStreamMaxCount = null,
            int? unidirectionalStreamMaxCount = null);

        /// <summary>Creates a connection using the underlying multiplexed client transport.</summary>
        /// <param name="endpoint">The connection endpoint.</param>
        /// <returns>The connection.</returns>
        IMultiplexedNetworkConnection CreateConnection(Endpoint endpoint);
    }

    public class SlicMultiplexedTransportTestFixture : IMultiplexedTransportTestFixture
    {
        private readonly SlicServerTransportOptions? _slicServerOptions;
        private readonly SlicClientTransportOptions? _slicClientOptions;

        public SlicMultiplexedTransportTestFixture(
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
            int? pauseWriterThreshold = null,
            int? resumeWriterThreshold = null,
            int? bidirectionalStreamMaxCount = null,
            int? unidirectionalStreamMaxCount = null)
        {
            SlicServerTransportOptions options = _slicServerOptions ?? new SlicServerTransportOptions();

            endpoint ??= options.SimpleServerTransport is TcpServerTransport ?
                Endpoint.FromString("icerpc://127.0.0.1:0/") :
                Endpoint.FromString($"icerpc://{Guid.NewGuid()}/");

            if (pauseWriterThreshold != null)
            {
                options.PauseWriterThreshold = pauseWriterThreshold.Value;
            }

            if (resumeWriterThreshold != null)
            {
                options.ResumeWriterThreshold = resumeWriterThreshold.Value;
            }

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
            return transport.Listen(endpoint.Value, null, NullLogger.Instance);
        }
    }
}

[Timeout(30000)]
[Parallelizable(ParallelScope.All)]
public class SlicOverTcpConformanceTests : MultiplexedTransportConformanceTests
{
    /// <summary>The multiplexed transports for conformance testing.</summary>
    public override IMultiplexedTransportTestFixture CreateMultiplexedTransportTestFixture() =>
        new SlicMultiplexedTransportTestFixture(
            new SlicServerTransportOptions
            {
                SimpleServerTransport = new TcpServerTransport()
            },
            new SlicClientTransportOptions
            {
                SimpleClientTransport = new TcpClientTransport()
            });
}

[Timeout(30000)]
[Parallelizable(ParallelScope.All)]
public class SlicOverColocConformanceTests : MultiplexedTransportConformanceTests
{
    /// <summary>The multiplexed transports for conformance testing.</summary>
    public override IMultiplexedTransportTestFixture CreateMultiplexedTransportTestFixture()
    {
        var coloc = new ColocTransport();
        return new SlicMultiplexedTransportTestFixture(
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
