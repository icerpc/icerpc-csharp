// Copyright (c) ZeroC, Inc.

using IceRpc.Slice;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using IceRpc.Transports.Internal;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Slic.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using System.Security.Authentication;

namespace IceRpc.Tests.Transports.Slic;

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
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        // Act/Assert
        Assert.That(
            async () => await sut.Client.CreateStreamAsync(bidirectional: true, default),
            Throws.TypeOf<InvalidOperationException>());
    }

    /// <summary>Verifies that accept stream fails if called before connect.</summary>
    [Test]
    public async Task Accepting_stream_before_calling_connect_fails()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        // Act/Assert
        Assert.That(
            async () => await sut.Client.AcceptStreamAsync(default),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task Connect_called_twice_throws_invalid_operation_exception()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        // Act/Assert
        Assert.That(async () => await sut.Client.ConnectAsync(default), Throws.TypeOf<InvalidOperationException>());
    }

    [TestCase(false, false, DuplexTransportOperations.Connect)]
    [TestCase(false, false, DuplexTransportOperations.Read)]
    [TestCase(false, true, DuplexTransportOperations.Connect)]
    [TestCase(true, false, DuplexTransportOperations.Connect)]
    [TestCase(true, false, DuplexTransportOperations.Read)]
    [TestCase(true, true, DuplexTransportOperations.Connect)]
    public async Task Connect_exception_handling_on_transport_failure(
        bool serverSide,
        bool authenticationException,
        DuplexTransportOperations operation)
    {
        // Arrange
        Exception exception = authenticationException ?
            new AuthenticationException() :
            new IceRpcException(IceRpcError.ConnectionRefused);

        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .AddTestDuplexTransportDecorator(
                clientOperationsOptions: new()
                {
                    Fail = serverSide ? DuplexTransportOperations.None : operation,
                    FailureException = exception
                },
                serverOperationsOptions: new()
                {
                    Fail = serverSide ? operation : DuplexTransportOperations.None,
                    FailureException = exception
                })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        Func<Task> connectCall = serverSide ?
            async () =>
            {
                using var clientConnectTcs = new CancellationTokenSource();
                Task connectTask = sut.Client.ConnectAsync(clientConnectTcs.Token);
                try
                {
                    await sut.AcceptAsync();
                }
                finally
                {
                    try
                    {
                        clientConnectTcs.Cancel();
                        await connectTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Prevents unobserved task exceptions.
                    }
                }
            }
        :
            async () =>
            {
                _ = AcceptAsync();
                _ = await sut.Client.ConnectAsync(default);

                async Task AcceptAsync()
                {
                    try
                    {
                        await sut.AcceptAsync();
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

    [Test]
    public async Task Connect_exception_handling_on_protocol_error([Values(false, true)] bool clientSide)
    {
        // Arrange
        var operationsOptions = new DuplexTransportOperationsOptions()
        {
            ReadDecorator = async (decoratee, buffer, cancellationToken) =>
                {
                    int count = await decoratee.ReadAsync(buffer, cancellationToken);
                    buffer.Span[0] = 0xFF; // Bogus frame type.
                    return count;
                }
        };

        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .AddTestDuplexTransportDecorator(
                serverOperationsOptions: clientSide ? null : operationsOptions,
                clientOperationsOptions: clientSide ? operationsOptions : null)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        Task connectTask;
        Task? clientConnectTask = null;
        using var clientConnectCts = new CancellationTokenSource();
        if (clientSide)
        {
            connectTask = sut.AcceptAndConnectAsync();
        }
        else
        {
            clientConnectTask = sut.Client.ConnectAsync(clientConnectCts.Token);
            connectTask = sut.AcceptAsync();
        }

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await connectTask);
        Assert.That(
            exception?.IceRpcError,
            Is.EqualTo(IceRpcError.IceRpcError),
            $"The test failed with an unexpected IceRpcError {exception}");

        if (clientConnectTask is not null)
        {
            clientConnectCts.Cancel();
            try
            {
                await clientConnectTask;
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    [TestCase(false, DuplexTransportOperations.Connect)]
    [TestCase(false, DuplexTransportOperations.Read)]
    [TestCase(false, DuplexTransportOperations.Write)]
    [TestCase(true, DuplexTransportOperations.Connect)]
    [TestCase(true, DuplexTransportOperations.Read)]
    public async Task Connect_cancellation_on_transport_hang(bool serverSide, DuplexTransportOperations operation)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .AddTestDuplexTransportDecorator(
                clientOperationsOptions: new() { Hold = serverSide ? DuplexTransportOperations.None : operation },
                serverOperationsOptions: new() { Hold = serverSide ? operation : DuplexTransportOperations.None })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        using var connectCts = new CancellationTokenSource(100);

        Func<Task> connectCall = serverSide ?
            async () =>
            {
                using var clientConnectTcs = new CancellationTokenSource();
                Task connectTask = sut.Client.ConnectAsync(clientConnectTcs.Token);
                try
                {
                    await sut.AcceptAsync(connectCts.Token);
                }
                finally
                {
                    try
                    {
                        clientConnectTcs.Cancel();
                        await connectTask;
                    }
                    catch (OperationCanceledException)
                    {
                        // Prevents unobserved task exceptions.
                    }
                }
            }
        :
            async () =>
            {
                _ = AcceptAsync();
                _ = await sut.Client.ConnectAsync(connectCts.Token);

                async Task AcceptAsync()
                {
                    try
                    {
                        await sut.AcceptAsync();
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

    /// <summary>Verifies that disabling the idle timeout doesn't abort the connection if it's idle.</summary>
    [Test]
    public async Task Connection_with_no_idle_timeout_is_not_aborted_when_idle()
    {
        // Arrange
        var services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.IdleTimeout = Timeout.InfiniteTimeSpan);
        services.AddOptions<SlicTransportOptions>("client").Configure(
            options => options.IdleTimeout = Timeout.InfiniteTimeSpan);

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        ValueTask<IMultiplexedStream> acceptStreamTask = sut.Server.AcceptStreamAsync(default);

        // Act
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert
        Assert.That(acceptStreamTask.IsCompleted, Is.False);
        await sut.Client.CloseAsync(MultiplexedConnectionCloseError.NoError, default);
        Assert.That(async () => await acceptStreamTask, Throws.InstanceOf<IceRpcException>());
    }

    /// <summary>Verifies that setting the idle timeout doesn't abort the connection if it's idle.</summary>
    [Test]
    public async Task Connection_with_idle_timeout_is_not_aborted_when_idle(
        [Values(true, false)] bool serverIdleTimeout)
    {
        // Arrange
        var services = new ServiceCollection().AddSlicTest();
        var idleTimeout = TimeSpan.FromSeconds(1);
        if (serverIdleTimeout)
        {
            services.AddOptions<SlicTransportOptions>("server").Configure(options => options.IdleTimeout = idleTimeout);
        }
        else
        {
            services.AddOptions<SlicTransportOptions>("client").Configure(options => options.IdleTimeout = idleTimeout);
        }

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        ValueTask<IMultiplexedStream> acceptStreamTask = sut.Server.AcceptStreamAsync(default);

        // Act
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert
        Assert.That(acceptStreamTask.IsCompleted, Is.False);
        await sut.Client.CloseAsync(MultiplexedConnectionCloseError.NoError, default);
        Assert.That(async () => await acceptStreamTask, Throws.InstanceOf<IceRpcException>());
    }

    /// <summary>Verifies the cancellation token of CloseAsync works when the ShutdownAsync of the underlying server
    /// duplex connection hangs.</summary>
    [Test]
    public async Task Close_canceled_when_duplex_server_connection_shutdown_hangs()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .AddTestDuplexTransportDecorator(
                serverOperationsOptions: new()
                {
                    Hold = DuplexTransportOperations.ShutdownWrite
                })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act
        Task closeTask = sut.Client.CloseAsync(MultiplexedConnectionCloseError.NoError, cts.Token);

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
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        // Act/Assert
        Assert.That(async () => await sut.Client.CloseAsync(0ul, default), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task ReadFrames_exception_handling_on_protocol_error()
    {
        // Arrange
        bool invalidRead = false;
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .AddTestDuplexTransportDecorator(
                serverOperationsOptions: new DuplexTransportOperationsOptions()
                {
                    ReadDecorator = async (decoratee, buffer, cancellationToken) =>
                        {
                            int count = await decoratee.ReadAsync(buffer, cancellationToken);
                            if (invalidRead)
                            {
                                buffer.Span[0] = 0xFF; // Bogus frame type.
                            }
                            return count;
                        }
                })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        invalidRead = true;
        IMultiplexedStream stream = await sut.Client.CreateStreamAsync(bidirectional: false, default);

        // Act
        _ = await stream.Output.WriteAsync(new byte[1], default);

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await sut.Server.AcceptStreamAsync(default));
        Assert.That(
            exception?.IceRpcError,
            Is.EqualTo(IceRpcError.IceRpcError),
            $"The test failed with an unexpected IceRpcError {exception}");
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
            pauseWriterThreshold: 16384,
            resumeWriterThreshold: 8192);
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

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        // Act
        await sut.AcceptAndConnectAsync();

        // Assert
        var serverConnection = (SlicConnection)sut.Server;
        var clientConnection = (SlicConnection)sut.Client;

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

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();
        using var streams = await sut.CreateAndAcceptStreamAsync();

        _ = await streams.Local.Output.WriteAsync(payload, default);

        // Act
        ValueTask<FlushResult> writeTask = streams.Local.Output.WriteAsync(payload, default);

        // Assert
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(writeTask.IsCompleted, Is.False);

        // Make sure the writeTask completes before completing the stream output.
        ReadResult readResult = await streams.Remote.Input.ReadAtLeastAsync(pauseThreshold - 1);
        Assert.That(readResult.IsCanceled, Is.False);
        streams.Remote.Input.AdvanceTo(readResult.Buffer.End);
        await writeTask;
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

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();
        using var streams1 = await sut.CreateAndAcceptStreamAsync();
        using var streams2 = await sut.CreateAndAcceptStreamAsync();

        _ = await streams1.Local.Output.WriteAsync(payload, default);
        ValueTask<FlushResult> writeTask = streams1.Local.Output.WriteAsync(payload, default);

        // Act

        // stream1 consumed all its send credit, this shouldn't affect stream2
        _ = await streams2.Local.Output.WriteAsync(payload, default);

        // Assert
        Assert.That(streams2.Local.Id, Is.EqualTo(streams2.Remote.Id));
        ReadResult readResult = await streams2.Remote.Input.ReadAtLeastAsync(pauseThreshold - 1);
        Assert.That(readResult.IsCanceled, Is.False);
        streams2.Remote.Input.AdvanceTo(readResult.Buffer.End);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(writeTask.IsCompleted, Is.False);

        // Make sure the writeTask completes before completing the stream output.
        readResult = await streams1.Remote.Input.ReadAtLeastAsync(pauseThreshold - 1);
        Assert.That(readResult.IsCanceled, Is.False);
        streams1.Remote.Input.AdvanceTo(readResult.Buffer.End);
        await writeTask;
    }

    /// <summary>Verifies that a pending write operation on the stream fails with <see cref="OperationCanceledException"
    /// /> when canceled.</summary>
    [Test]
    public async Task Stream_write_cancellation()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .AddTestDuplexTransportDecorator()
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        var duplexClientConnection =
            provider.GetRequiredService<TestDuplexClientTransportDecorator>().LastCreatedConnection;

        using var streams = await sut.CreateAndAcceptStreamAsync();

        using var writeCts = new CancellationTokenSource();

        // Act
        duplexClientConnection.Operations.Hold = DuplexTransportOperations.Write;
        ValueTask<FlushResult> writeTask = streams.Local.Output.WriteAsync(new byte[128 * 1024], writeCts.Token);
        writeCts.Cancel();

        // Assert
        Assert.That(
            async () => await writeTask,
            Throws.InstanceOf<OperationCanceledException>().With.Property("CancellationToken").EqualTo(writeCts.Token));
        duplexClientConnection.Operations.Hold = DuplexTransportOperations.None;
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

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        using var streams = await sut.CreateAndAcceptStreamAsync();

        ValueTask<FlushResult> writeTask = streams.Local.Output.WriteAsync(new byte[pauseThreshold], default);
        ReadResult readResult = await streams.Remote.Input.ReadAtLeastAsync(pauseThreshold - resumeThreshold, default);

        // Act
        streams.Remote.Input.AdvanceTo(readResult.Buffer.GetPosition(pauseThreshold - resumeThreshold));

        // Assert
        Assert.That(async () => await writeTask, Throws.Nothing);
    }
}
