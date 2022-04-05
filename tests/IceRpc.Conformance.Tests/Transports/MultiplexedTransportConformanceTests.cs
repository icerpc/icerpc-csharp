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
    public async Task Accept_a_stream_initiated_by_the_server_connection()
    {
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection sut = transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAndAcceptAsync(sut, listener);
        IMultiplexedStream serverStream = serverConnection.CreateStream(true);
        await serverStream.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream clientStream = await sut.AcceptStreamAsync(default);

        Assert.That(serverStream.Id, Is.EqualTo(clientStream.Id));

        await CompleteStreamAsync(clientStream);
        await CompleteStreamAsync(serverStream);
    }

    [Test]
    public async Task Accept_a_stream_initiated_by_the_client_connection()
    {
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection =
            transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection sut = await ConnectAndAcceptAsync(clientConnection, listener);
        IMultiplexedStream clientStream = clientConnection.CreateStream(true);
        await clientStream.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream serverStream = await sut.AcceptStreamAsync(default);

        Assert.That(serverStream.Id, Is.EqualTo(clientStream.Id));

        await CompleteStreamAsync(clientStream);
        await CompleteStreamAsync(serverStream);
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
    public async Task Disposing_the_connection_aborts_the_streams()
    {
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection sut = transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAndAcceptAsync(sut, listener);
        IMultiplexedStream clientStream = sut.CreateStream(true);
        await clientStream.Output.WriteAsync(_oneBytePayload);

        await sut.DisposeAsync();

        Assert.ThrowsAsync<ObjectDisposedException>(async () => await clientStream.Input.ReadAsync());
        await CompleteStreamAsync(clientStream);
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
    public async Task Stream_full_duplex_communication(
        [Values(1, 16, 32, 64)] int segments,
        [Values(1, 256, 64 * 1024)] int payloadSize)
    {
        // Arrange
        IMultiplexedTransportProvider transportProvider = CreateMultiplexedTransportProvider();
        await using IListener<IMultiplexedNetworkConnection> listener = transportProvider.CreateListener();
        await using IMultiplexedNetworkConnection clientConnection =
            transportProvider.CreateConnection(listener.Endpoint);
        await using IMultiplexedNetworkConnection serverConnection = await ConnectAndAcceptAsync(clientConnection, listener);

        IMultiplexedStream clientStream = clientConnection.CreateStream(bidirectional: true);
        _ = await clientStream.Output.WriteAsync(_oneBytePayload, default);
        IMultiplexedStream serverStream = await serverConnection.AcceptStreamAsync(default);

        var payload = new ReadOnlyMemory<byte>(
            Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray());

        // Act
        Task clientWriteTask = WriteAsync(clientStream, payload);
        Task serverReadTask = ReadAsync(serverStream, payloadSize * segments);
        Task serverWriteTask = WriteAsync(serverStream, payload);
        Task clientReadTask = ReadAsync(clientStream, payloadSize * segments);

        // Assert
        await Task.WhenAll(clientWriteTask, serverWriteTask, clientReadTask, serverReadTask);

        async Task ReadAsync(IMultiplexedStream stream, long size)
        {
            while (size > 0)
            {
                ReadResult readResult = await stream.Input.ReadAsync();
                size -= readResult.Buffer.Length;
                stream.Input.AdvanceTo(readResult.Buffer.End, readResult.Buffer.End);
            }
            await stream.Input.CompleteAsync();
        }

        async Task WriteAsync(IMultiplexedStream stream, ReadOnlyMemory<byte> payload)
        {
            for (int i = 0; i < segments; ++i)
            {
                await stream.Output.WriteAsync(payload, default);
            }
            await stream.Output.CompleteAsync();
        }
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

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public class SlicOverColocConformanceTests : MultiplexedTransportConformanceTests
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
