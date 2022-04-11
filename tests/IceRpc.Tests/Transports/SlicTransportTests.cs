// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Transports.Tests;

/// <summary>Unit tests for slic transport.</summary>
[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public class SlicTransportTests
{
    [TestCase(1024 * 32)]
    [TestCase(1024 * 512)]
    [TestCase(1024 * 1024)]
    public async Task Stream_write_blocks_after_consuming_the_send_credit(
        int pauseThreshold)
    {
        // Arrange
        await using var serviceProvider = CreateServiceCollection(
            serverOptions: new SlicServerTransportOptions
            {
                PauseWriterThreshold = pauseThreshold
            }).BuildServiceProvider();

        byte[] payload = new byte[pauseThreshold - 1];

        Task<IMultiplexedNetworkConnection> acceptTask = serviceProvider.GetMultiplexedServerConnectionAsync();
        await using IMultiplexedNetworkConnection clientConnection =
            await serviceProvider.GetMultiplexedClientConnectionAsync();
        await using IMultiplexedNetworkConnection serverConnection = await acceptTask;

        (IMultiplexedStream localStream, IMultiplexedStream remoteStream) =
            await CreateAndAcceptStreamAsync(serverConnection, clientConnection);
        FlushResult result = await localStream.Output.WriteAsync(payload, default);

        // Act
        ValueTask<FlushResult> writeTask = localStream.Output.WriteAsync(payload, default);

        // Assert
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(writeTask.IsCompleted, Is.False);

        CompleteStreams(localStream, remoteStream);
    }

    [TestCase(32 * 1024)]
    [TestCase(512 * 1024)]
    [TestCase(1024 * 1024)]
    public async Task Stream_write_blocking_does_not_affect_concurrent_streams(
        int pauseThreshold)
    {
        // Arrange
        byte[] payload = new byte[pauseThreshold - 1];
        await using ServiceProvider serviceProvider = CreateServiceCollection(
            serverOptions: new SlicServerTransportOptions
            {
                PauseWriterThreshold = pauseThreshold
            }).BuildServiceProvider();

        Task<IMultiplexedNetworkConnection> acceptTask = serviceProvider.GetMultiplexedServerConnectionAsync();
        await using IMultiplexedNetworkConnection clientConnection =
            await serviceProvider.GetMultiplexedClientConnectionAsync();
        await using IMultiplexedNetworkConnection serverConnection = await acceptTask;

        (IMultiplexedStream localStream1, IMultiplexedStream remoteStream1) =
            await CreateAndAcceptStreamAsync(serverConnection, clientConnection);
        (IMultiplexedStream localStream2, IMultiplexedStream remoteStream2) =
            await CreateAndAcceptStreamAsync(serverConnection, clientConnection);

        FlushResult result = await localStream1.Output.WriteAsync(payload, default);
        ValueTask<FlushResult> writeTask = localStream1.Output.WriteAsync(payload, default);

        // Act

        // stream1 consumed all its send credit, this shouldn't affect stream2
        result = await localStream2.Output.WriteAsync(payload, default);

        // Assert
        Assert.That(localStream2.Id, Is.EqualTo(remoteStream2.Id));
        ReadResult readResult = await remoteStream2.Input.ReadAtLeastAsync(pauseThreshold - 1);
        Assert.That(readResult.IsCanceled, Is.False);
        remoteStream2.Input.AdvanceTo(readResult.Buffer.End);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(writeTask.IsCompleted, Is.False);
    }

    [TestCase(64 * 1024, 32 * 1024)]
    [TestCase(1024 * 1024, 512 * 1024)]
    [TestCase(2048 * 1024, 512 * 1024)]
    public async Task Write_resumes_after_reaching_the_resume_writer_threshold(
        int pauseThreshold,
        int resumeThreshold)
    {
        // Arrange
        await using ServiceProvider serviceProvider = CreateServiceCollection(
            serverOptions: new SlicServerTransportOptions
            {
                PauseWriterThreshold = pauseThreshold,
                ResumeWriterThreshold = resumeThreshold,
            }).BuildServiceProvider();

        Task<IMultiplexedNetworkConnection>? acceptTask = serviceProvider.GetMultiplexedServerConnectionAsync();
        await using IMultiplexedNetworkConnection clientConnection =
            await serviceProvider.GetMultiplexedClientConnectionAsync();
        await using IMultiplexedNetworkConnection serverConnection = await acceptTask;

        IMultiplexedStream stream = clientConnection.CreateStream(bidirectional: true);

        (IMultiplexedStream localStream, IMultiplexedStream remoteStream) =
            await CreateAndAcceptStreamAsync(serverConnection, clientConnection);

        ValueTask<FlushResult> writeTask = localStream.Output.WriteAsync(new byte[pauseThreshold], default);
        ReadResult readResult = await remoteStream.Input.ReadAtLeastAsync(pauseThreshold - resumeThreshold, default);

        // Act
        remoteStream.Input.AdvanceTo(readResult.Buffer.GetPosition(pauseThreshold - resumeThreshold));

        // Assert
        Assert.That(async () => await writeTask, Throws.Nothing);
    }

    private static void CompleteStreams(params IMultiplexedStream[] streams)
    {
        foreach (IMultiplexedStream stream in streams)
        {
            stream.Output.Complete();
            stream.Input.Complete();
        }
    }

    private static async Task<(IMultiplexedStream LocalStream, IMultiplexedStream RemoteStream)> CreateAndAcceptStreamAsync(
        IMultiplexedNetworkConnection remoteConnection,
        IMultiplexedNetworkConnection localConnection,
        bool bidirectional = true)
    {
        IMultiplexedStream localStream = localConnection.CreateStream(bidirectional);
        _ = await localStream.Output.WriteAsync(new byte[1]);
        IMultiplexedStream remoteStream = await remoteConnection.AcceptStreamAsync(default);
        ReadResult readResult = await remoteStream.Input.ReadAsync();
        remoteStream.Input.AdvanceTo(readResult.Buffer.End);
        return (localStream, remoteStream);
    }

    private static IServiceCollection CreateServiceCollection(
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
