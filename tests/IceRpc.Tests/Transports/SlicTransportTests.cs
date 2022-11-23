// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Tests.Transports;

/// <summary>Unit tests for slic transport.</summary>
[Parallelizable(ParallelScope.All)]
public class SlicTransportTests
{
    /// <summary>Verifies that create stream fails if called before connect.</summary>
    [Test]
    public async Task Create_stream_before_calling_connect_fails()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<SlicConnection>();

        // Act/Assert
        Assert.That(
            async () => await sut.CreateStreamAsync(bidirectional: true, default),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>Verifies that accept stream fails if called before connect.</summary>
    [Test]
    public async Task Accepting_stream_before_calling_connect_fails()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<SlicConnection>();

        // Act/Assert
        Assert.That(async () => await sut.AcceptStreamAsync(default), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task Connect_called_twice_throws_invalid_operation_exception()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<SlicConnection>();

        var clientConnection = provider.GetRequiredService<SlicConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        Task<IMultiplexedConnection> acceptTask = ConnectAndAcceptConnectionAsync(listener, clientConnection);

        // Act/Assert
        Assert.That(async () => await sut.ConnectAsync(default), Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>Verifies that close fails if called before connect.</summary>
    [Test]
    public async Task Closing_connection_before_calling_connect_fails()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<SlicConnection>();

        // Act/Assert
        Assert.That(async () => await sut.CloseAsync(0ul, default), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task Stream_peer_options_are_set_after_connect()
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(options =>
            {
                options.PauseWriterThreshold = 6893;
                options.ResumeWriterThreshold = 2000;
                options.PacketMaxSize = 2098;
            });
        services.AddOptions<SlicTransportOptions>("client").Configure(options =>
            {
                options.PauseWriterThreshold = 2405;
                options.ResumeWriterThreshold = 2000;
                options.PacketMaxSize = 4567;
            });
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var clientConnection = provider.GetRequiredService<SlicConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        Task<IMultiplexedConnection> acceptTask = ConnectAndAcceptConnectionAsync(listener, clientConnection);

        // Act
        var serverConnection = (SlicConnection)await acceptTask;

        // Assert
        Assert.That(serverConnection.PeerPauseWriterThreshold, Is.EqualTo(2405));
        Assert.That(clientConnection.PeerPauseWriterThreshold, Is.EqualTo(6893));
        Assert.That(serverConnection.PeerPacketMaxSize, Is.EqualTo(4567));
        Assert.That(clientConnection.PeerPacketMaxSize, Is.EqualTo(2098));
    }

    [TestCase(1024 * 32)]
    [TestCase(1024 * 512)]
    [TestCase(1024 * 1024)]
    public async Task Stream_write_blocks_after_consuming_the_send_credit(int pauseThreshold)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.PauseWriterThreshold = pauseThreshold);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        byte[] payload = new byte[pauseThreshold - 1];

        var clientConnection = provider.GetRequiredService<SlicConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        Task<IMultiplexedConnection> acceptTask = ConnectAndAcceptConnectionAsync(listener, clientConnection);
        await using IMultiplexedConnection serverConnection = await acceptTask;

        (IMultiplexedStream localStream, IMultiplexedStream remoteStream) =
            await CreateAndAcceptStreamAsync(clientConnection, serverConnection);
        _ = await localStream.Output.WriteAsync(payload, default);

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
    public async Task Stream_write_blocking_does_not_affect_concurrent_streams(int pauseThreshold)
    {
        // Arrange
        byte[] payload = new byte[pauseThreshold - 1];
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.PauseWriterThreshold = pauseThreshold);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var clientConnection = provider.GetRequiredService<SlicConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        Task<IMultiplexedConnection> acceptTask = ConnectAndAcceptConnectionAsync(listener, clientConnection);
        await using IMultiplexedConnection serverConnection = await acceptTask;

        (IMultiplexedStream localStream1, IMultiplexedStream remoteStream1) =
            await CreateAndAcceptStreamAsync(clientConnection, serverConnection);
        (IMultiplexedStream localStream2, IMultiplexedStream remoteStream2) =
            await CreateAndAcceptStreamAsync(clientConnection, serverConnection);

        _ = await localStream1.Output.WriteAsync(payload, default);
        ValueTask<FlushResult> writeTask = localStream1.Output.WriteAsync(payload, default);

        // Act

        // stream1 consumed all its send credit, this shouldn't affect stream2
        _ = await localStream2.Output.WriteAsync(payload, default);

        // Assert
        Assert.That(localStream2.Id, Is.EqualTo(remoteStream2.Id));
        ReadResult readResult = await remoteStream2.Input.ReadAtLeastAsync(pauseThreshold - 1);
        Assert.That(readResult.IsCanceled, Is.False);
        remoteStream2.Input.AdvanceTo(readResult.Buffer.End);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(writeTask.IsCompleted, Is.False);
    }

    /// <summary>Verifies that canceling writes when the write is waiting to acquire the max stream semaphore correctly
    /// raise OperationCanceledException.</summary>
    [Test]
    public async Task Stream_write_max_streams_semaphores_cancellation()
    {
        // Arrange
        IServiceCollection serviceCollection = new ServiceCollection().AddSlicTest();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.MaxBidirectionalStreams = 0);
        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);

        var clientConnection = provider.GetRequiredService<SlicConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        Task<IMultiplexedConnection> acceptTask = ConnectAndAcceptConnectionAsync(listener, clientConnection);
        await using IMultiplexedConnection serverConnection = await acceptTask;

        IMultiplexedStream clientStream = await clientConnection.CreateStreamAsync(
            bidirectional: true,
            default).ConfigureAwait(false);
        using var cts = new CancellationTokenSource();
        ValueTask<FlushResult> task = clientStream.Output.WriteAsync(new byte[1], cts.Token);

        // Act
        cts.Cancel();

        // Act/Assert
        Assert.CatchAsync<OperationCanceledException>(async () => await task);
    }

    [TestCase(64 * 1024, 32 * 1024)]
    [TestCase(1024 * 1024, 512 * 1024)]
    [TestCase(2048 * 1024, 512 * 1024)]
    public async Task Write_resumes_after_reaching_the_resume_writer_threshold(
        int pauseThreshold,
        int resumeThreshold)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(options =>
            {
                options.PauseWriterThreshold = pauseThreshold;
                options.ResumeWriterThreshold = resumeThreshold;
            });
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var clientConnection = provider.GetRequiredService<SlicConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        Task<IMultiplexedConnection> acceptTask = ConnectAndAcceptConnectionAsync(listener, clientConnection);
        await using IMultiplexedConnection serverConnection = await acceptTask;

        (IMultiplexedStream localStream, IMultiplexedStream remoteStream) =
            await CreateAndAcceptStreamAsync(clientConnection, serverConnection);

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
        IMultiplexedConnection localConnection,
        IMultiplexedConnection remoteConnection,
        bool bidirectional = true)
    {
        IMultiplexedStream localStream = await localConnection.CreateStreamAsync(
            bidirectional,
            default).ConfigureAwait(false);
        _ = await localStream.Output.WriteAsync(new byte[1]);
        IMultiplexedStream remoteStream = await remoteConnection.AcceptStreamAsync(default);
        ReadResult readResult = await remoteStream.Input.ReadAsync();
        remoteStream.Input.AdvanceTo(readResult.Buffer.End);
        return (localStream, remoteStream);
    }

    private static async Task<IMultiplexedConnection> ConnectAndAcceptConnectionAsync(
        IListener<IMultiplexedConnection> listener,
        IMultiplexedConnection connection)
    {
        var connectTask = connection.ConnectAsync(default);
        var serverConnection = (await listener.AcceptAsync(default)).Connection;
        await serverConnection.ConnectAsync(default);
        await connectTask;
        return serverConnection;
    }
}
