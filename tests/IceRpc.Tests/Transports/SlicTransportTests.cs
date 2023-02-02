// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Security.Authentication;

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
        await ConnectAndAcceptConnectionAsync(listener, clientConnection);

        // Act/Assert
        Assert.That(async () => await sut.ConnectAsync(default), Throws.TypeOf<InvalidOperationException>());
    }

    [TestCase(false, false, DuplexTransportOperation.Connect)]
    [TestCase(false, false, DuplexTransportOperation.Read)]
    [TestCase(false, false, DuplexTransportOperation.Write)]
    [TestCase(false, true, DuplexTransportOperation.Connect)]
    [TestCase(true, false, DuplexTransportOperation.Connect)]
    [TestCase(true, false, DuplexTransportOperation.Write)]
    [TestCase(true, false, DuplexTransportOperation.Read)]
    [TestCase(true, true, DuplexTransportOperation.Connect)]
    public async Task Connect_exception_handling_on_transport_failure(
        bool serverSide,
        bool authenticationException,
        DuplexTransportOperation operation)
    {
        // Arrange
        Exception exception = authenticationException ?
            new AuthenticationException() :
            new IceRpcException(IceRpcError.ConnectionRefused);

        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .AddTestDuplexTransport(
                clientFailOperation: serverSide ? DuplexTransportOperation.None : operation,
                clientFailureException: exception,
                serverFailOperation: serverSide ? operation : DuplexTransportOperation.None,
                serverFailureException: exception)
            .BuildServiceProvider(validateScopes: true);

        var clientConnection = provider.GetRequiredService<SlicConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        Func<Task> connectCall = serverSide ?
            async () => _ = await ConnectAndAcceptConnectionAsync(listener, clientConnection) :
            async () =>
            {
                _ = AcceptAsync();
                _ = await clientConnection.ConnectAsync(default);

                async Task AcceptAsync()
                {
                    try
                    {
                        (IMultiplexedConnection connection, EndPoint _) = await listener.AcceptAsync(default);
                        await using IMultiplexedConnection serverConnection = connection;
                        await serverConnection.ConnectAsync(default);
                    }
                    catch
                    {
                        // Prevents unobserved task exceptions.
                    }
                }
            };

        // Act/Assert
        Exception? caughtException = Assert.CatchAsync(() => connectCall());
        Assert.That(caughtException, Is.EqualTo(exception));
    }

    [TestCase(false, DuplexTransportOperation.Connect)]
    [TestCase(false, DuplexTransportOperation.Read)]
    [TestCase(false, DuplexTransportOperation.Write)]
    [TestCase(true, DuplexTransportOperation.Connect)]
    [TestCase(true, DuplexTransportOperation.Write)]
    [TestCase(true, DuplexTransportOperation.Read)]
    public async Task Connect_cancellation_on_transport_hang(bool serverSide, DuplexTransportOperation operation)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .AddTestDuplexTransport(
                clientHoldOperation: serverSide ? DuplexTransportOperation.None : operation,
                serverHoldOperation: serverSide ? operation : DuplexTransportOperation.None)
            .BuildServiceProvider(validateScopes: true);

        var clientConnection = provider.GetRequiredService<SlicConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        using var connectCts = new CancellationTokenSource(100);

        Func<Task> connectCall = serverSide ?
            async () => _ = await ConnectAndAcceptConnectionAsync(listener, clientConnection, connectCts.Token) :
            async () =>
            {
                _ = AcceptAsync();
                _ = await clientConnection.ConnectAsync(connectCts.Token);

                async Task AcceptAsync()
                {
                    try
                    {
                        (IMultiplexedConnection connection, EndPoint _) = await listener.AcceptAsync(default);
                        await using IMultiplexedConnection serverConnection = connection;
                        await serverConnection.ConnectAsync(default);
                    }
                    catch
                    {
                        // Prevents unobserved task exceptions.
                    }
                }
            };

        // Act/Assert
        Assert.That(
            () => connectCall(),
            Throws.InstanceOf<OperationCanceledException>().With.Property(
                "CancellationToken").EqualTo(connectCts.Token));
    }

    /// <summary>Verifies the cancellation token of CloseAsync works when the ShutdownAsync of the underlying server
    /// duplex connection hangs.</summary>
    [Test]
    public async Task Close_canceled_when_duplex_server_connection_shutdown_hangs()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .AddTestDuplexTransport(serverHoldOperation: DuplexTransportOperation.Shutdown)
            .BuildServiceProvider(validateScopes: true);

        var clientConnection = provider.GetRequiredService<SlicConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        Task<IMultiplexedConnection> acceptTask = ConnectAndAcceptConnectionAsync(listener, clientConnection);
        var serverConnection = (SlicConnection)await acceptTask;
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act
        Task closeTask = clientConnection.CloseAsync(MultiplexedConnectionCloseError.NoError, cts.Token);

        // Assert
        Assert.That(async () => await closeTask, Throws.InstanceOf<OperationCanceledException>());
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
    public async Task Send_initialize_frame_with_unsupported_slic_version_replies_with_version_frame()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
          .AddSlicTest()
          .BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            new ServerAddress(Protocol.IceRpc) { Host = "colochost" },
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        await duplexClientConnection.ConnectAsync(default);

        await using var writer = new DuplexConnectionWriter(
            duplexClientConnection,
            MemoryPool<byte>.Shared,
            4096,
            keepAliveAction: null);
        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);

        // Act
        EncodeInitializeFrame(writer, version: 2);
        await writer.FlushAsync(default);
        (var multiplexedServerConnection, _) = await acceptTask;
        var connectTask = multiplexedServerConnection.ConnectAsync(default);
        (FrameType frameType, int frameSize, VersionBody versionBody) = await ReadFrameHeaderAsync(reader);
        EncodeInitializeFrame(writer, version: 1);
        await writer.FlushAsync(default);

        // Assert
        Assert.That(frameType, Is.EqualTo(FrameType.Version));
        Assert.That(versionBody.Versions, Is.EqualTo(new ulong[] { 1 }));
        Assert.That(() => connectTask, Throws.InstanceOf<IceRpcException>()); // The initialize frame is incomplete.

        await multiplexedServerConnection.DisposeAsync();

        void EncodeInitializeFrame(IBufferWriter<byte> writer, ulong version)
        {
            var initializeBody = new InitializeBody(Protocol.IceRpc.Name, new Dictionary<ParameterKey, IList<byte>>());
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
            encoder.EncodeFrameType(FrameType.Initialize);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encoder.EncodeVarUInt62(version);
            initializeBody.Encode(ref encoder);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
        }

        async Task<(FrameType FrameType, int FrameSize, VersionBody VersionBody)> ReadFrameHeaderAsync(
            DuplexConnectionReader reader)
        {
            while (true)
            {
                // Read data from the pipe reader.
                if (!reader.TryRead(out ReadOnlySequence<byte> buffer))
                {
                    buffer = await reader.ReadAsync(default);
                }

                if (TryDecodeHeader(
                    buffer,
                    out (FrameType FrameType, int FrameSize) header,
                    out int consumed))
                {
                    reader.AdvanceTo(buffer.GetPosition(consumed));
                    buffer = await reader.ReadAtLeastAsync(header.FrameSize);
                    return (header.FrameType, header.FrameSize, DecodeVersionBody(buffer));
                }
                else
                {
                    reader.AdvanceTo(buffer.Start, buffer.End);
                }
            }
        }

        static VersionBody DecodeVersionBody(ReadOnlySequence<byte> buffer)
        {
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
            var versionBody = new VersionBody(ref decoder);
            decoder.CheckEndOfBuffer(skipTaggedParams: false);
            return versionBody;
        }

        static bool TryDecodeHeader(
            ReadOnlySequence<byte> buffer,
            out (FrameType FrameType, int FrameSize) header,
            out int consumed)
        {
            header = default;
            consumed = default;

            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);

            // Decode the frame type and frame size.
            if (!decoder.TryDecodeUInt8(out byte frameType) ||
                !decoder.TryDecodeSize(out header.FrameSize))
            {
                return false;
            }
            header.FrameType = frameType.AsFrameType();

            consumed = (int)decoder.Consumed;
            return true;
        }
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

        // Make sure the writeTask completes before completing the stream output.
        ReadResult readResult = await remoteStream.Input.ReadAtLeastAsync(pauseThreshold - 1);
        Assert.That(readResult.IsCanceled, Is.False);
        remoteStream.Input.AdvanceTo(readResult.Buffer.End);
        await writeTask;

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

        // Make sure the writeTask completes before completing the stream output.
        readResult = await remoteStream1.Input.ReadAtLeastAsync(pauseThreshold - 1);
        Assert.That(readResult.IsCanceled, Is.False);
        remoteStream1.Input.AdvanceTo(readResult.Buffer.End);
        await writeTask;

        CompleteStreams(localStream1, remoteStream1, localStream2, remoteStream2);
    }

    [Test]
    public async Task Stream_write_cancellation_does_not_cancel_write_if_writing_data_on_duplex_connection()
    {
        // Arrange

        var colocTransport = new ColocTransport();
        var clientTransport = new TestDuplexClientTransportDecorator(colocTransport.ClientTransport);

        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .AddSingleton(colocTransport.ServerTransport)
            .AddSingleton<IDuplexClientTransport>(clientTransport)
            .BuildServiceProvider(validateScopes: true);

        var clientConnection = provider.GetRequiredService<SlicConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        Task<IMultiplexedConnection> acceptTask = ConnectAndAcceptConnectionAsync(listener, clientConnection);
        await using IMultiplexedConnection serverConnection = await acceptTask;
        TestDuplexConnectionDecorator duplexClientConnection = clientTransport.LastConnection;

        (IMultiplexedStream localStream, IMultiplexedStream remoteStream) =
            await CreateAndAcceptStreamAsync(clientConnection, serverConnection);

        using var writeCts = new CancellationTokenSource();

        // Act
        duplexClientConnection.HoldOperation = DuplexTransportOperation.Write;
        ValueTask<FlushResult> writeTask = localStream.Output.WriteAsync(new byte[1], writeCts.Token);
        writeCts.Cancel();
        await Task.Delay(TimeSpan.FromMilliseconds(10));

        // Assert
        Assert.That(writeTask.IsCompleted, Is.False);
        duplexClientConnection.HoldOperation = DuplexTransportOperation.None;
        Assert.That(async () => await writeTask, Throws.Nothing);

        CompleteStreams(localStream, remoteStream);
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

        CompleteStreams(localStream, remoteStream);
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
        IMultiplexedConnection connection,
        CancellationToken cancellationToken = default)
    {
        Task<TransportConnectionInformation> connectTask = connection.ConnectAsync(cancellationToken);
        IMultiplexedConnection serverConnection = (await listener.AcceptAsync(cancellationToken)).Connection;
        try
        {
            await serverConnection.ConnectAsync(cancellationToken);
            await connectTask;
            return serverConnection;
        }
        catch
        {
            await serverConnection.DisposeAsync();
            try
            {
                await connectTask;
            }
            catch
            {
            }
            throw;
        }
    }
}
