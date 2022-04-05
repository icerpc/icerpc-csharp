// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Tests;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Transports.Tests;

/// <summary>Unit tests for slic transport.</summary>
[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public class SlicTransportTests
{
    private static readonly ReadOnlyMemory<byte> _oneMbPayload = new(
        Enumerable.Range(0, 1024 * 1024).Select(i => (byte)(i % 256)).ToArray());

    [Test]
    public async Task Stream_write_blocks_after_consuming_the_send_credit()
    {
        // Arrange
        await using var serviceProvider = CreateSlicTransportServiceCollection(
            serverOptions: new SlicServerTransportOptions
            {
                PauseWriterThreshold = 1024 * 1024
            }).BuildServiceProvider();

        var acceptTask = serviceProvider.GetMultiplexedServerConnectionAsync();
        IMultiplexedNetworkConnection sut = await serviceProvider.GetMultiplexedClientConnectionAsync();
        IMultiplexedNetworkConnection serverConnection = await acceptTask;

        IMultiplexedStream stream = sut.CreateStream(bidirectional: true);
        FlushResult result = await stream.Output.WriteAsync(_oneMbPayload, default);

        // Act
        ValueTask<FlushResult> writeTask = stream.Output.WriteAsync(_oneMbPayload, default);

        // Assert
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(writeTask.IsCompleted, Is.False);
    }

    [Test]
    public async Task Stream_write_blocking_does_not_affect_concurrent_streams()
    {
        // Arrange
        await using var serviceProvider = CreateSlicTransportServiceCollection(
            serverOptions: new SlicServerTransportOptions
            {
                PauseWriterThreshold = 1024 * 1024
            }).BuildServiceProvider();

        var acceptTask = serviceProvider.GetMultiplexedServerConnectionAsync();
        IMultiplexedNetworkConnection sut = await serviceProvider.GetMultiplexedClientConnectionAsync();
        IMultiplexedNetworkConnection serverConnection = await acceptTask;

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
        await using var serviceProvider = CreateSlicTransportServiceCollection(
            serverOptions: new SlicServerTransportOptions
            {
                PauseWriterThreshold = 1024 * 1024,
                ResumeWriterThreshold = 1024 * 512,
            }).BuildServiceProvider();

        var acceptTask = serviceProvider.GetMultiplexedServerConnectionAsync();
        IMultiplexedNetworkConnection sut = await serviceProvider.GetMultiplexedClientConnectionAsync();
        IMultiplexedNetworkConnection serverConnection = await acceptTask;

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
    public async Task Peer_options_are_set_after_connect()
    {
        // Arrange
        await using ServiceProvider serviceProvider = CreateSlicTransportServiceCollection(
            new SlicServerTransportOptions
            {
                PauseWriterThreshold = 6893,
                ResumeWriterThreshold = 2000,
                PacketMaxSize = 2098
            },
            new SlicClientTransportOptions
            {
                PauseWriterThreshold = 2405,
                ResumeWriterThreshold = 2000,
                PacketMaxSize = 4567
            }).BuildServiceProvider();
        Task<IMultiplexedNetworkConnection> acceptTask = serviceProvider.GetMultiplexedServerConnectionAsync();
        var clientConnection = (SlicNetworkConnection)await serviceProvider.GetMultiplexedClientConnectionAsync();

        // Act
        var serverConnection = (SlicNetworkConnection) await acceptTask;

        // Assert
        Assert.That(serverConnection.PeerPauseWriterThreshold, Is.EqualTo(2405));
        Assert.That(clientConnection.PeerPauseWriterThreshold, Is.EqualTo(6893));
        Assert.That(serverConnection.PeerPacketMaxSize, Is.EqualTo(4567));
        Assert.That(clientConnection.PeerPacketMaxSize, Is.EqualTo(2098));
    }

    private static IServiceCollection CreateSlicTransportServiceCollection(
        SlicServerTransportOptions? serverOptions = null,
        SlicClientTransportOptions? clientOptions = null)
    {
        var coloc = new ColocTransport();

        serverOptions ??= new SlicServerTransportOptions();
        serverOptions.SimpleServerTransport ??= coloc.ServerTransport;

        clientOptions ??= new SlicClientTransportOptions();
        clientOptions.SimpleClientTransport ??= coloc.ClientTransport;

        return new TransportServiceCollection()
            .AddTransient(_ => clientOptions)
            .AddTransient(_ => serverOptions);
    }
}
