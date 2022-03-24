// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace IceRpc.Transports.Tests;

[Parallelizable(ParallelScope.All)]
[Timeout(1000)]
public sealed class SlicNetworkConnectionTests
{
    [Test]
    public async Task Connect_to_remote_endpoint()
    {
        await using IListener<IMultiplexedNetworkConnection> listener = CreateListener();
        await using IMultiplexedNetworkConnection sut = CreateConnection(listener.Endpoint);

        var connectTask = sut.ConnectAsync(default);

        var serverConnection = await listener.AcceptAsync();
        await serverConnection.ConnectAsync(default);
        Assert.That(async () => await connectTask, Throws.Nothing);
    }

    [Test]
    public async Task Connect_iddle_timeout()
    {
        await using IListener<IMultiplexedNetworkConnection> listener = CreateListener(
            tcpOptions: new TcpServerTransportOptions
            {
                IdleTimeout = TimeSpan.FromSeconds(15)
            });
        
        await using var sut = CreateConnection(
            listener.Endpoint,
            tcpOtions: new TcpClientTransportOptions
            {
                IdleTimeout = TimeSpan.FromSeconds(30)
            });

        var connectTask = sut.ConnectAsync(default);

        var serverConnection = await listener.AcceptAsync();
        await serverConnection.ConnectAsync(default);
        await connectTask;

        Assert.That(sut.IdleTimeout, Is.EqualTo(TimeSpan.FromSeconds(15)));
    }

    [TestCase(1)]
    [TestCase(1024)]
    public async Task Max_concurrent_bidirectional_streams(int maxStreamCount)
    {
        // Arrange
        await using IListener<IMultiplexedNetworkConnection> listener = CreateListener(
            options: new SlicServerTransportOptions
            {
                BidirectionalStreamMaxCount = maxStreamCount,
            });
        await using IMultiplexedNetworkConnection sut = CreateConnection(listener.Endpoint);

        Task<NetworkConnectionInformation> connectTask = sut.ConnectAsync(default);
        IMultiplexedNetworkConnection serverConnection = await listener.AcceptAsync();
        await serverConnection.ConnectAsync(default);
        await connectTask;

        var payload = new ReadOnlyMemory<byte>(new byte[] { 0xFF });

        var streams = new List<IMultiplexedStream>();
        for (int i = 0; i < maxStreamCount; i++)
        {
            IMultiplexedStream stream = sut.CreateStream(bidirectional: true);
            streams.Add(stream);
            await stream.Output.WriteAsync(payload);
        }

        // Act
        IMultiplexedStream lastStream = sut.CreateStream(bidirectional: true);

        // Assert

        // The last stream cannot start as we have already rich the max concurrent streams
        streams.Add(lastStream);
        Assert.That(async () =>
            {
                var cancellationSource = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
                await lastStream.Output.WriteAsync(payload, cancellationSource.Token);
            },
            Throws.TypeOf<OperationCanceledException>());
        
        await streams[0].Input.CompleteAsync();
        await streams[0].Output.CompleteAsync();
        streams.RemoveAt(0);
        Assert.That(async () => await lastStream.Output.WriteAsync(payload), Throws.Nothing);
        
        foreach (IMultiplexedStream stream in streams)
        {
            Assert.That(stream.IsStarted, Is.True);
            await stream.Input.CompleteAsync();
            await stream.Output.CompleteAsync();
        }
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
}
