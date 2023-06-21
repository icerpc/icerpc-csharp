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

    /// <summary>Verifies that ConnectAsync can be canceled.</summary>
    [Test]
    public async Task Connect_cancellation()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);

        using var cts = new CancellationTokenSource();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
        var clientConnection = clientTransport.CreateConnection(
            listener.ServerAddress,
            new MultiplexedConnectionOptions(),
            clientAuthenticationOptions: null);
        var connectTask = clientConnection.ConnectAsync(cts.Token);

        // Act
        cts.Cancel();

        // Assert
        Assert.That(async () => await connectTask, Throws.InstanceOf<OperationCanceledException>());
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
            listener.ServerAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        await duplexClientConnection.ConnectAsync(default);

        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);
        var writer = new MemoryBufferWriter(new byte[1024]);

        // Act
        EncodeInitializeFrame(writer, version: 2);
        await duplexClientConnection.WriteAsync(new ReadOnlySequence<byte>(writer.WrittenMemory), default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var _ = multiplexedServerConnection;
        var connectTask = multiplexedServerConnection.ConnectAsync(default);
        (FrameType frameType, int frameSize, VersionBody versionBody) = await ReadFrameHeaderAsync(reader);
        writer.Clear();
        EncodeInitializeFrame(writer, version: 1);
        await duplexClientConnection.WriteAsync(new ReadOnlySequence<byte>(writer.WrittenMemory), default);

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

    /// <summary>This test verifies that stream flow control prevents a new stream from being created under the
    /// following conditions:
    /// - the max stream count is reached
    /// - writes are closed on both the local and remote streams
    /// - data on the remote stream is not consumed
    ///
    /// CreateStreamAsync should unblock only once the application consumed all the buffered data from the remote
    /// stream.
    ///
    /// With Slic, once all the buffered data is consumed, a StreamReadsClosed frame is sent to notify the client
    /// that it can create a new stream.</summary>
    // TODO: Move to MultiplexedConnectionConformanceTests once Quic is fixed.
    [Test]
    public async Task Stream_count_is_not_decremented_until_remote_data_is_consumed()
    {
        // Arrange
        IServiceCollection serviceCollection = new ServiceCollection().AddSlicTest();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.MaxBidirectionalStreams = 1);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        var payload = new ReadOnlyMemory<byte>(new byte[16 * 1024]);

        IMultiplexedStream localStream = await sut.Client.CreateStreamAsync(true, default);
        await ((ReadOnlySequencePipeWriter)localStream.Output).WriteAsync(
            new ReadOnlySequence<byte>(payload),
            endStream: true,
            default); // Ensures the stream is started.

        IMultiplexedStream remoteStream = await sut.Server.AcceptStreamAsync(default);
        remoteStream.Output.Complete();

        ReadResult readResult = await localStream.Input.ReadAsync();
        Assert.That(readResult.IsCompleted);
        localStream.Input.Complete();

        // At this point only the remote stream input is not completed. The stream holds 16KB of buffered data.
        // CreateStreamAsync should block because the data isn't consumed.

        // Act/Assert
        Assert.That(
            async () =>
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

                // This should throw OperationCanceledException because reads from the remote stream accepted above are
                // not closed (the remote stream still has non-consumed buffered data).
                _ = await sut.Client.CreateStreamAsync(bidirectional: true, cts.Token);
            },
            Throws.InstanceOf<OperationCanceledException>());

        // Complete the input and check that the next stream creation works.
        remoteStream.Input.Complete();

        localStream = await sut.Client.CreateStreamAsync(true, default);
        localStream.Input.Complete();
        localStream.Output.Complete();
    }

    [Test]
    public async Task Stream_peer_options_are_set_after_connect()
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(options =>
            {
                options.InitialStreamWindowSize = 6893;
                options.PacketMaxSize = 2098;
            });
        services.AddOptions<SlicTransportOptions>("client").Configure(options =>
            {
                options.InitialStreamWindowSize = 2405;
                options.PacketMaxSize = 4567;
            });
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();

        // Act
        await sut.AcceptAndConnectAsync();

        // Assert
        var serverConnection = (SlicConnection)sut.Server;
        var clientConnection = (SlicConnection)sut.Client;

        Assert.That(serverConnection.PeerInitialStreamWindowSize, Is.EqualTo(2405));
        Assert.That(clientConnection.PeerInitialStreamWindowSize, Is.EqualTo(6893));
        Assert.That(serverConnection.PeerPacketMaxSize, Is.EqualTo(4567));
        Assert.That(clientConnection.PeerPacketMaxSize, Is.EqualTo(2098));
    }

    [TestCase(1024 * 32)]
    [TestCase(1024 * 512)]
    [TestCase(1024 * 1024)]
    public async Task Stream_write_blocks_after_consuming_the_send_credit(int windowSize)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.InitialStreamWindowSize = windowSize);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        byte[] payload = new byte[windowSize - 1];

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
        ReadResult readResult = await streams.Remote.Input.ReadAtLeastAsync(windowSize - 1);
        Assert.That(readResult.IsCanceled, Is.False);
        streams.Remote.Input.AdvanceTo(readResult.Buffer.End);
        await writeTask;
    }

    [TestCase(32 * 1024)]
    [TestCase(512 * 1024)]
    [TestCase(1024 * 1024)]
    public async Task Stream_write_blocking_does_not_affect_concurrent_streams(int windowSize)
    {
        // Arrange
        byte[] payload = new byte[windowSize - 1];
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.InitialStreamWindowSize = windowSize);
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
        ReadResult readResult = await streams2.Remote.Input.ReadAtLeastAsync(windowSize - 1);
        Assert.That(readResult.IsCanceled, Is.False);
        streams2.Remote.Input.AdvanceTo(readResult.Buffer.End);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(writeTask.IsCompleted, Is.False);

        // Make sure the writeTask completes before completing the stream output.
        readResult = await streams1.Remote.Input.ReadAtLeastAsync(windowSize - 1);
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

    [Test]
    public async Task Write_resumes_after_reaching_the_resume_writer_threshold()
    {
        int windowSize = 32 * 1024;

        // Arrange
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.InitialStreamWindowSize = windowSize);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        using var streams = await sut.CreateAndAcceptStreamAsync();

        ValueTask<FlushResult> writeTask = streams.Local.Output.WriteAsync(new byte[windowSize + 1024], default);
        ReadResult readResult = await streams.Remote.Input.ReadAtLeastAsync(windowSize, default);

        // Act
        streams.Remote.Input.AdvanceTo(readResult.Buffer.GetPosition(windowSize));

        // Assert
        Assert.That(async () => await writeTask, Throws.Nothing);
    }

    [Test]
    public async Task Close_cannot_complete_before_duplex_connection_writes_are_closed()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .AddTestDuplexTransportDecorator(
                clientOperationsOptions: new()
                {
                    Hold = DuplexTransportOperations.ShutdownWrite
                })
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        var duplexClientConnection =
            provider.GetRequiredService<TestDuplexClientTransportDecorator>().LastCreatedConnection;

        // Act
        var closeTask = sut.Client.CloseAsync(0, CancellationToken.None);
        await Task.Delay(TimeSpan.FromMilliseconds(50)); // Give time to CloseAsync to proceed.

        // Assert
        Assert.That(closeTask.IsCompleted, Is.False);
    }

    [Test]
    public async Task Close_aborts_pending_stream_read_when_close_is_called_while_data_is_read_on_the_duplex_connection()
    {
        // Arrange

        // Allow the Slic connection to read 512 bytes on the duplex connection before it starts holding reads. This
        // allows performing the connection establishment and to read the beginning of the Stream frame. The goal is
        // to get the read call on the duplex connection while reading the stream frame data.
        int allowedReadLength = 512;
        using var readSemaphore = new SemaphoreSlim(initialCount: 0);

        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .AddTestDuplexTransportDecorator(clientOperationsOptions: new DuplexTransportOperationsOptions()
                {
                    ReadDecorator = async (connection, memory, cancellationToken) =>
                        {
                            if (allowedReadLength == 0)
                            {
                                await readSemaphore.WaitAsync(-1, cancellationToken);
                                allowedReadLength = int.MaxValue;
                            }

                            if (allowedReadLength < memory.Length)
                            {
                                memory = memory[0..allowedReadLength];
                            }
                            int length = await connection.ReadAsync(memory, cancellationToken);
                            allowedReadLength -= length;
                            return length;
                        }
                })
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        var duplexClientConnection =
            provider.GetRequiredService<TestDuplexClientTransportDecorator>().LastCreatedConnection;

        // Start a new stream and write 4KB to the server. The client duplex connection reads will hang while reading
        // the stream data.
        using var streams = await sut.CreateAndAcceptStreamAsync();
        var readTask = streams.Local.Input.ReadAsync();
        await streams.Remote.Output.WriteAsync(new byte[4096]);

        // Act
        Task closeTask = sut.Client.CloseAsync(0, CancellationToken.None);

        // Assert
        Assert.That(async () => await readTask, Throws.InstanceOf<IceRpcException>());
        readSemaphore.Release();
        Assert.That(() => closeTask, Throws.Nothing);
    }

    [Test]
    public async Task Dispose_connection_when_duplex_connection_shutdown_write_hangs()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .AddTestDuplexTransportDecorator(
                clientOperationsOptions: new()
                {
                    Hold = DuplexTransportOperations.ShutdownWrite
                })
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        var duplexClientConnection =
            provider.GetRequiredService<TestDuplexClientTransportDecorator>().LastCreatedConnection;

        // Act
        Assert.That(sut.Client.DisposeAsync, Throws.Nothing);
    }
}
