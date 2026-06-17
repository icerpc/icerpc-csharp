// Copyright (c) ZeroC, Inc.

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
using ZeroC.Slice.Codec;
using ZeroC.Tests.Common;

namespace IceRpc.Tests.Transports.Slic;

/// <summary>Unit tests for slic transport.</summary>
[Parallelizable(ParallelScope.All)]
public class SlicTransportTests
{
    private static IEnumerable<TestCaseData> InvalidConnectionFrames
    {
        get
        {
            // TestCase(FrameType frameType, bool emptyBody)

            yield return new TestCaseData(nameof(FrameType.Close), false);
            yield return new TestCaseData(nameof(FrameType.Close), true);
            yield return new TestCaseData(nameof(FrameType.Ping), false);
            yield return new TestCaseData(nameof(FrameType.Ping), true);
            yield return new TestCaseData(nameof(FrameType.Pong), false);
            yield return new TestCaseData(nameof(FrameType.Pong), true);
        }
    }

    private static IEnumerable<TestCaseData> ConnectionProtocolErrors
    {
        get
        {
            // Unexpected frames after connection establishment.
            foreach (FrameType frameType in new FrameType[]
            {
                FrameType.Initialize,
                FrameType.InitializeAck,
                FrameType.Version,
            })
            {
                yield return new TestCaseData(
                    (IDuplexConnection connection) => WriteFrameAsync(connection, frameType),
                    $"Received unexpected {frameType} frame.");
            }

            // Pong frame without Ping frame.
            yield return new TestCaseData(
                (IDuplexConnection connection) => WriteFrameAsync(connection, FrameType.Pong, new PongBody(64).Encode),
                $"Received unexpected {nameof(FrameType.Pong)} frame.");

            // Stream frames which are not supposed to be sent on a non-started stream.
            foreach (FrameType frameType in new FrameType[]
            {
                FrameType.StreamReadsClosed,
                FrameType.StreamWindowUpdate,
                FrameType.StreamWritesClosed,
            })
            {
                yield return new TestCaseData(
                    (IDuplexConnection connection) => WriteStreamFrameAsync(connection, frameType, streamId: 4ul),
                    $"Received {frameType} frame for unknown stream.");
            }

            // Stream frame opening a new remote bidirectional stream with a non-initial stream ID.
            // The first remote bidirectional stream ID the server expects from the client is 0.
            yield return new TestCaseData(
                (IDuplexConnection connection) => WriteStreamFrameAsync(
                    connection,
                    FrameType.Stream,
                    streamId: 4ul,
                    (ref SliceEncoder encoder) => encoder.EncodeBool(false)),
                "Invalid stream ID.");

            // Stream frame opening a new remote unidirectional stream with a non-initial stream ID.
            // The first remote unidirectional stream ID the server expects from the client is 2.
            yield return new TestCaseData(
                (IDuplexConnection connection) => WriteStreamFrameAsync(
                    connection,
                    FrameType.Stream,
                    streamId: 6ul,
                    (ref SliceEncoder encoder) => encoder.EncodeBool(false)),
                "Invalid stream ID.");

            // Bogus stream frame with FrameSize=0 (stream ID not encoded)
            yield return new TestCaseData(
                (IDuplexConnection connection) => WriteFrameAsync(
                    connection,
                    FrameType.StreamLast,
                    (ref SliceEncoder encoder) => { }),
                "Invalid stream frame size.");

            // Encode a stream frame with a frame size inferior to the stream ID size.
            yield return new TestCaseData(
                (IDuplexConnection connection) =>
                {
                    var writer = new MemoryBufferWriter(new byte[1024]);
                    {
                        var encoder = new SliceEncoder(writer);
                        encoder.EncodeFrameType(FrameType.Stream);
                        encoder.EncodeSize(1);
                        encoder.EncodeVarUInt62(int.MaxValue);
                    }
                    return connection.WriteAsync(new ReadOnlySequence<byte>(writer.WrittenMemory), default).AsTask();
                },
                "Invalid stream frame size.");
        }
    }

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
    public async Task Connect_exception_handling_on_protocol_error([Values] bool clientSide)
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
            listener.TransportAddress,
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

    [Test]
    public async Task Connect_with_slic_version_unsupported_by_the_server()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
          .AddSlicTest()
          .BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        await duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var _ = multiplexedServerConnection;

        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);

        // Write the initialize frame.
        await WriteInitializeFrameAsync(duplexClientConnection, version: 2);

        // Connect the server connection to read the initialize frame. It will send the version frame since the version
        // 2 is not supported.
        var connectTask = multiplexedServerConnection.ConnectAsync(default);

        // Read the version frame.
        await ReadFrameAsync(reader);

        // Act

        // Shutdown the client connection because it doesn't support any of the versions returned by the server.
        await duplexClientConnection.ShutdownWriteAsync(default);

        // Assert
        Assert.That(
            () => connectTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionRefused));
    }

    [Test]
    public async Task Connect_with_server_that_returns_unsupported_slic_version()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
          .AddSlicTest()
          .BuildServiceProvider(validateScopes: true);

        var multiplexedClientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
        var duplexServerTransport = provider.GetRequiredService<IDuplexServerTransport>();
        await using var listener = duplexServerTransport.Listen(
            new TransportAddress { Host = "::1" },
            options: new(),
            serverAuthenticationOptions: null);
        var acceptTask = listener.AcceptAsync(default);
        await using var multiplexedClientConnection = multiplexedClientTransport.CreateConnection(
            listener.TransportAddress,
            new MultiplexedConnectionOptions(),
            clientAuthenticationOptions: null);
        var connectTask = multiplexedClientConnection.ConnectAsync(default);
        (var duplexServerConnection, var transportConnectionInformation) = await acceptTask;
        using var _ = duplexServerConnection;
        using var reader = new DuplexConnectionReader(duplexServerConnection, MemoryPool<byte>.Shared, 4096);

        // Read the initialize frame
        await ReadFrameAsync(reader);

        // Act

        // Write the version frame with versions unsupported by the client.
        await WriteFrameAsync(duplexServerConnection, FrameType.Version, new VersionBody(new ulong[] { 3 }).Encode);

        // Assert
        Assert.That(
            () => connectTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionRefused));
    }

    [Test]
    public async Task Connect_with_server_that_returns_invalid_slic_version()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
          .AddSlicTest()
          .BuildServiceProvider(validateScopes: true);

        var multiplexedClientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
        var duplexServerTransport = provider.GetRequiredService<IDuplexServerTransport>();
        await using var listener = duplexServerTransport.Listen(
            new TransportAddress { Host = "::1" },
            options: new(),
            serverAuthenticationOptions: null);
        var acceptTask = listener.AcceptAsync(default);
        await using var multiplexedClientConnection = multiplexedClientTransport.CreateConnection(
            listener.TransportAddress,
            new MultiplexedConnectionOptions(),
            clientAuthenticationOptions: null);
        var connectTask = multiplexedClientConnection.ConnectAsync(default);
        (var duplexServerConnection, var transportConnectionInformation) = await acceptTask;
        using var _ = duplexServerConnection;
        using var reader = new DuplexConnectionReader(duplexServerConnection, MemoryPool<byte>.Shared, 4096);

        // Read the initialize frame
        await ReadFrameAsync(reader);

        // Act

        // Write the version frame with the version requested by the client in the initialize frame.
        await WriteFrameAsync(duplexServerConnection, FrameType.Version, new VersionBody(new ulong[] { 3, 1 }).Encode);

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await connectTask);
        Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.IceRpcError));
        Assert.That(exception.Message, Is.EqualTo("The connection was aborted by a Slic protocol error."));
    }

    [Test]
    public async Task Connect_version_negotiation()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
          .AddSlicTest()
          .BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        await duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var _ = multiplexedServerConnection;

        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);

        // Act

        // Write initialize frame
        await WriteInitializeFrameAsync(duplexClientConnection, version: 2);

        // Connect server connection, it doesn't support the client version so should return a version frame.
        var connectTask = multiplexedServerConnection.ConnectAsync(default);
        (FrameType versionFrameType, ReadOnlySequence<byte> versionBuffer) = await ReadFrameAsync(reader);

        // Write back an initialize version with a supported version.
        await WriteInitializeFrameAsync(duplexClientConnection, version: 1);

        // Wait and read the initialize ack frame from the server.
        (FrameType initializeAckFrameType, ReadOnlySequence<byte> _) = await ReadFrameAsync(reader);

        // Assert
        Assert.That(versionFrameType, Is.EqualTo(FrameType.Version));
        Assert.That(DecodeVersionBody(versionBuffer).Versions, Is.EqualTo(new ulong[] { 1 }));
        Assert.That(initializeAckFrameType, Is.EqualTo(FrameType.InitializeAck));
        Assert.That(() => connectTask, Throws.Nothing);

        static VersionBody DecodeVersionBody(ReadOnlySequence<byte> buffer)
        {
            var decoder = new SliceDecoder(buffer);
            var versionBody = new VersionBody(ref decoder);
            decoder.CheckEndOfBuffer();
            return versionBody;
        }
    }

    /// <summary>Verifies that disabling the idle timeout doesn't abort the connection if it's idle.</summary>
    [Test]
    [NonParallelizable]
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

    /// <summary>Verifies that setting the idle timeout doesn't abort the connection if there is no application-level
    /// activity.</summary>
    [Test]
    [NonParallelizable]
    public async Task Connection_with_idle_timeout_is_not_aborted_when_inactive([Values] bool serverIdleTimeout)
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
        if (acceptStreamTask.IsCompleted)
        {
            // Unexpected.
            Assert.DoesNotThrowAsync(async () => await acceptStreamTask); // to display the cause of the failure.
            Assert.Fail("acceptStreamTask should not be completed");
        }

        await sut.Client.CloseAsync(MultiplexedConnectionCloseError.NoError, default);

        // Successful completion.
        Assert.That(
            async () => await acceptStreamTask,
            Throws.InstanceOf<IceRpcException>()
                .With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionClosedByPeer));
    }

    /// <summary>Verifies that setting the idle timeout doesn't abort the connection even when there is slow write
    /// activity from client to server. This slow write-only activity could possibly prevent the sending of Ping frames
    /// from the client to the server, or prevent the server from reading these Ping frames.</summary>
    [Test]
    [NonParallelizable]
    public async Task Connection_with_idle_timeout_and_slow_write_is_not_aborted([Values] bool serverIdleTimeout)
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
        IMultiplexedStream stream = await sut.Client.CreateStreamAsync(bidirectional: false, default);

        // Act
        for (int i = 0; i < 4; ++i)
        {
            // Slow writes to the server (the server doesn't even read them).
            _ = await stream.Output.WriteAsync(new byte[1], default);
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
        stream.Output.Complete();
        ValueTask<IMultiplexedStream> nextAcceptStreamTask = sut.Server.AcceptStreamAsync(default);

        // Assert
        Assert.That(acceptStreamTask.IsCompleted, Is.True);
        if (nextAcceptStreamTask.IsCompleted)
        {
            // Unexpected. See #4108.
            Assert.DoesNotThrowAsync(async () => await nextAcceptStreamTask);
            Assert.Fail("nextAcceptStreamTask should not be completed");
        }
        await sut.Client.CloseAsync(MultiplexedConnectionCloseError.NoError, default);

        // Successful completion.
        Assert.That(
            async () => await nextAcceptStreamTask,
            Throws.InstanceOf<IceRpcException>()
                .With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionClosedByPeer));
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
    [TestCaseSource(nameof(ConnectionProtocolErrors))]
    public async Task Connection_protocol_errors(
        Func<IDuplexConnection, Task> protocolAction,
        string expectedErrorMessage)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        Task connectTask = duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var _ = multiplexedServerConnection;
        await connectTask;
        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);

        // Connect the multiplexed connection.
        await WriteInitializeFrameAsync(duplexClientConnection, version: 1);
        await multiplexedServerConnection.ConnectAsync(default);
        await ReadFrameAsync(reader);
        ValueTask<IMultiplexedStream> acceptStreamTask = multiplexedServerConnection.AcceptStreamAsync(default);

        // Act
        await protocolAction(duplexClientConnection);

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await acceptStreamTask);
        Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.IceRpcError));
        Assert.That(exception.Message, Is.EqualTo("The connection was aborted by a Slic protocol error."));
        Assert.That(exception.InnerException?.Message, Is.EqualTo(expectedErrorMessage));
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
    public void Setting_max_stream_frame_size_above_limit_throws()
    {
        var options = new SlicTransportOptions();
        Assert.That(() => options.MaxStreamFrameSize = SlicTransportOptions.MaxStreamFrameSizeCeiling + 1, Throws.ArgumentException);
    }

    [Test]
    public async Task Reject_peer_max_stream_frame_size_above_limit()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        await duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var serverConnectionHandle = multiplexedServerConnection;

        // Act - send an Initialize frame that advertises a MaxStreamFrameSize above the allowed limit.
        await WriteFrameAsync(
            duplexClientConnection,
            FrameType.Initialize,
            (ref SliceEncoder encoder) =>
            {
                var parameters = new Dictionary<ParameterKey, IList<byte>>();
                byte[] oversizedMaxStreamFrameSizeBuffer = new byte[8];
                SliceEncoder.EncodeVarUInt62(
                    (ulong)SlicTransportOptions.MaxStreamFrameSizeCeiling + 1,
                    oversizedMaxStreamFrameSizeBuffer);
                parameters[ParameterKey.MaxStreamFrameSize] = oversizedMaxStreamFrameSizeBuffer;
                byte[] initialStreamWindowSizeBuffer = new byte[4];
                SliceEncoder.EncodeVarUInt62(32 * 1024, initialStreamWindowSizeBuffer);
                parameters[ParameterKey.InitialStreamWindowSize] = initialStreamWindowSizeBuffer;

                var initializeBody = new InitializeBody(parameters);
                encoder.EncodeVarUInt62(1); // version
                initializeBody.Encode(ref encoder);
            });

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await multiplexedServerConnection.ConnectAsync(default));
        Assert.That(exception!.InnerException, Is.InstanceOf<InvalidDataException>());
        Assert.That(exception.InnerException!.Message, Does.Contain("cannot exceed"));
    }

    [Test]
    public async Task Reject_window_update_causing_overflow()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        Task connectTask = duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var serverConnectionHandle = multiplexedServerConnection;
        await connectTask;
        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);

        // Advertise InitialStreamWindowSize = MaxWindowSize so the server's outgoing SlicPipeWriter starts its
        // _peerWindowSize at int.MaxValue; any positive increment then overflows.
        await WriteFrameAsync(
            duplexClientConnection,
            FrameType.Initialize,
            (ref SliceEncoder encoder) =>
            {
                var parameters = new Dictionary<ParameterKey, IList<byte>>();
                byte[] maxStreamFrameSizeBuffer = new byte[4];
                SliceEncoder.EncodeVarUInt62(4096, maxStreamFrameSizeBuffer);
                parameters[ParameterKey.MaxStreamFrameSize] = maxStreamFrameSizeBuffer;
                byte[] initialStreamWindowSizeBuffer = new byte[8];
                SliceEncoder.EncodeVarUInt62(
                    (ulong)SlicTransportOptions.MaxWindowSize,
                    initialStreamWindowSizeBuffer);
                parameters[ParameterKey.InitialStreamWindowSize] = initialStreamWindowSizeBuffer;

                var initializeBody = new InitializeBody(parameters);
                encoder.EncodeVarUInt62(1); // version
                initializeBody.Encode(ref encoder);
            });
        var serverConnectTask = multiplexedServerConnection.ConnectAsync(default);
        await ReadFrameAsync(reader); // consume InitializeAck
        await serverConnectTask;

        // Open a bidirectional stream so the server has a SlicPipeWriter whose _peerWindowSize == MaxWindowSize.
        await WriteStreamFrameAsync(
            duplexClientConnection,
            FrameType.Stream,
            streamId: 0ul,
            (ref SliceEncoder encoder) => encoder.EncodeBool(false));
        IMultiplexedStream acceptedStream = await multiplexedServerConnection.AcceptStreamAsync(default);

        // Act - send a StreamWindowUpdate that pushes the server's cumulative window over int.MaxValue.
        await WriteStreamFrameAsync(
            duplexClientConnection,
            FrameType.StreamWindowUpdate,
            streamId: 0ul,
            (ref SliceEncoder encoder) => new StreamWindowUpdateBody(1).Encode(ref encoder));

        // Assert - the overflow rejection tears down the connection. Depending on whether AcceptStreamAsync
        // observes the close state via the already-set _isClosed path or via the accept-channel completion, it
        // may surface the close error (ConnectionAborted) or the original SlicPipeWriter exception (IceRpcError),
        // so accept either.
        IceRpcException exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await multiplexedServerConnection.AcceptStreamAsync(default))!;
        Assert.That(
            exception.IceRpcError,
            Is.EqualTo(IceRpcError.ConnectionAborted).Or.EqualTo(IceRpcError.IceRpcError));

        acceptedStream.Output.Complete();
        acceptedStream.Input.Complete();
    }

    [Test]
    public async Task Reject_stream_frame_exceeding_max_size()
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.MaxStreamFrameSize = 1024);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        Task connectTask = duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var serverConnectionHandle = multiplexedServerConnection;
        await connectTask;
        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);

        await WriteInitializeFrameAsync(duplexClientConnection, version: 1);
        var serverConnectTask = multiplexedServerConnection.ConnectAsync(default);
        await ReadFrameAsync(reader); // consume InitializeAck
        await serverConnectTask;

        // Act - send a stream frame whose body exceeds the server's local MaxStreamFrameSize (1024). The frame body
        // is (streamId varint + payload), so a 2048-byte payload guarantees size > 1024.
        const int payloadSize = 2048;
        var writer = new MemoryBufferWriter(new byte[payloadSize + 16]);
        {
            var encoder = new SliceEncoder(writer);
            encoder.EncodeFrameType(FrameType.Stream);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encoder.EncodeVarUInt62(0ul); // streamId
            encoder.WriteByteSpan(new byte[payloadSize]);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
        }
        await duplexClientConnection.WriteAsync(new ReadOnlySequence<byte>(writer.WrittenMemory), default);

        // Assert - server rejects with an InvalidDataException, surfaced as IceRpcError.
        Assert.That(
            async () => await multiplexedServerConnection.AcceptStreamAsync(default),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.IceRpcError));
    }

    [Test]
    public async Task Stream_peer_options_are_set_after_connect()
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(options =>
            {
                options.InitialStreamWindowSize = 6893;
                options.MaxStreamFrameSize = 2098;
            });
        services.AddOptions<SlicTransportOptions>("client").Configure(options =>
            {
                options.InitialStreamWindowSize = 2405;
                options.MaxStreamFrameSize = 4567;
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
        Assert.That(serverConnection.PeerMaxStreamFrameSize, Is.EqualTo(4567));
        Assert.That(clientConnection.PeerMaxStreamFrameSize, Is.EqualTo(2098));
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

    /// <summary>Verifies that when a write on the underlying duplex connection fails, a subsequent fire-and-forget
    /// stream frame (here the StreamLast frame sent when the stream's output is completed) closes the Slic connection
    /// instead of letting the transport exception go unobserved.</summary>
    [Test]
    public async Task Duplex_connection_write_failure_from_fire_and_forget_stream_frame_closes_connection()
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

        // Write data so the stream is started and the first background write succeeds.
        _ = await streams.Local.Output.WriteAsync(new byte[1], default);

        // Park the background writer task on its next write to the duplex connection.
        Task writeCalled = duplexClientConnection.Operations.GetCalledTask(DuplexTransportOperations.Write);
        duplexClientConnection.Operations.Hold = DuplexTransportOperations.Write;
        _ = await streams.Local.Output.WriteAsync(new byte[1], default);
        await writeCalled;

        // Act

        // Configure the write to fail and release the parked write. The background writer task fails, completes its
        // pipe reader with the transport exception and exits.
        duplexClientConnection.Operations.Fail = DuplexTransportOperations.Write;
        duplexClientConnection.Operations.Hold = DuplexTransportOperations.None;

        // Give the background writer task time to fail and complete its pipe reader.
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        // Completing the stream sends a StreamLast/StreamReadsClosed frame through the fire-and-forget write path,
        // whose flush rethrows the transport exception.
        streams.Local.Output.Complete();
        streams.Local.Input.Complete();

        // Assert

        // The fire-and-forget write path observes the transport failure and closes the connection promptly, so
        // accepting a stream fails with an IceRpcException.
        using var acceptCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        Assert.That(
            async () => await sut.Client.AcceptStreamAsync(acceptCts.Token),
            Throws.InstanceOf<IceRpcException>());
    }

    [Test]
    public async Task Reject_slic_control_frame_with_oversized_body()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        Task connectTask = duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var _ = multiplexedServerConnection;
        await connectTask;

        // Act - Write an Initialize frame header that declares a body larger than MaxControlFrameBodySize (16,383).
        await WriteOversizedFrameAsync(duplexClientConnection, FrameType.Initialize, 16_384);

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await multiplexedServerConnection.ConnectAsync(default));
        Assert.That(exception!.InnerException, Is.InstanceOf<InvalidDataException>());
        Assert.That(
            exception.InnerException!.Message,
            Does.Contain("exceeds the maximum allowed size"));
    }

    [Test]
    public async Task Write_initialize_frame_with_empty_body()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        Task connectTask = duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var _ = multiplexedServerConnection;
        await connectTask;

        // Act
        await WriteFrameAsync(duplexClientConnection, FrameType.Initialize, (ref SliceEncoder encoder) => { });

        // Assert
        Assert.That(
            async () => await multiplexedServerConnection.ConnectAsync(default),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.IceRpcError));
    }

    [TestCase(nameof(FrameType.InitializeAck))]
    [TestCase(nameof(FrameType.Version))]
    public async Task Write_initialize_ack_or_version_frame_with_empty_body(string frameTypeStr)
    {
        FrameType frameType = Enum.Parse<FrameType>(frameTypeStr);

        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
          .AddSlicTest()
          .BuildServiceProvider(validateScopes: true);

        var multiplexedClientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
        var duplexServerTransport = provider.GetRequiredService<IDuplexServerTransport>();
        await using var listener = duplexServerTransport.Listen(
            new TransportAddress { Host = "::1" },
            options: new(),
            serverAuthenticationOptions: null);
        var acceptTask = listener.AcceptAsync(default);
        await using var multiplexedClientConnection = multiplexedClientTransport.CreateConnection(
            listener.TransportAddress,
            new MultiplexedConnectionOptions(),
            clientAuthenticationOptions: null);
        var connectTask = multiplexedClientConnection.ConnectAsync(default);
        (var duplexServerConnection, var transportConnectionInformation) = await acceptTask;
        using var _ = duplexServerConnection;

        // Act
        await WriteFrameAsync(duplexServerConnection, frameType, (ref SliceEncoder encoder) => { });

        // Assert
        Assert.That(
            async () => await connectTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.IceRpcError));
    }

    [Test]
    [TestCaseSource(nameof(InvalidConnectionFrames))]
    public async Task Write_connection_frame_with_invalid_body(string frameTypeStr, bool emptyBody)
    {
        FrameType frameType = Enum.Parse<FrameType>(frameTypeStr);

        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        Task connectTask = duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var _ = multiplexedServerConnection;
        await connectTask;
        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);

        // Connect the multiplexed connection.
        await WriteInitializeFrameAsync(duplexClientConnection, version: 1);
        await multiplexedServerConnection.ConnectAsync(default);
        (FrameType initializeAckFrameType, ReadOnlySequence<byte> _) = await ReadFrameAsync(reader);

        var body = new ReadOnlyMemory<byte>(new byte[256]);

        // Act
        await WriteFrameAsync(
            duplexClientConnection,
            frameType,
            (ref SliceEncoder encoder) => encoder.WriteByteSpan(emptyBody ? ReadOnlySpan<byte>.Empty : body.Span));

        // Assert
        Assert.That(
            async () => await multiplexedServerConnection.AcceptStreamAsync(default),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.IceRpcError));
    }

    [TestCase(nameof(FrameType.Stream))]
    [TestCase(nameof(FrameType.StreamWritesClosed))]
    [TestCase(nameof(FrameType.StreamReadsClosed))]
    public async Task Write_stream_frame_with_invalid_body(string frameTypeStr)
    {
        FrameType frameType = Enum.Parse<FrameType>(frameTypeStr);
        bool emptyBody = frameType == FrameType.Stream;

        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        Task connectTask = duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var _ = multiplexedServerConnection;
        await connectTask;
        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);

        // Connect the multiplexed connection.
        await WriteInitializeFrameAsync(duplexClientConnection, version: 1);
        await multiplexedServerConnection.ConnectAsync(default);
        (FrameType initializeAckFrameType, ReadOnlySequence<byte> _) = await ReadFrameAsync(reader);

        var body = new ReadOnlyMemory<byte>(new byte[256]);

        // Start the stream.
        await WriteStreamFrameAsync(
            duplexClientConnection,
            FrameType.Stream,
            streamId: 0ul,
            (ref SliceEncoder encoder) => encoder.EncodeBool(false));

        IMultiplexedStream acceptedStream = await multiplexedServerConnection.AcceptStreamAsync(default);

        // Act
        await WriteStreamFrameAsync(
            duplexClientConnection,
            frameType,
            streamId: 0ul,
            (ref SliceEncoder encoder) => encoder.WriteByteSpan(emptyBody ? ReadOnlySpan<byte>.Empty : body.Span));

        // Assert
        Assert.That(
            async () => await multiplexedServerConnection.AcceptStreamAsync(default),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.IceRpcError));

        acceptedStream?.Output.Complete();
        acceptedStream?.Input.Complete();
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

    [Test]
    public void PauseWriterThreshold_default_value()
    {
        var options = new SlicTransportOptions();
        Assert.That(options.PauseWriterThreshold, Is.EqualTo(65_536));
    }

    [Test]
    public void PauseWriterThreshold_below_minimum_throws()
    {
        var options = new SlicTransportOptions();
        Assert.That(() => options.PauseWriterThreshold = 1023, Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void PauseWriterThreshold_negative_throws()
    {
        var options = new SlicTransportOptions();
        Assert.That(() => options.PauseWriterThreshold = -1, Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void PauseWriterThreshold_at_minimum_succeeds()
    {
        var options = new SlicTransportOptions();
        Assert.That(() => options.PauseWriterThreshold = 1024, Throws.Nothing);
        Assert.That(options.PauseWriterThreshold, Is.EqualTo(1024));
    }

    [Test]
    public void PauseWriterThreshold_zero_succeeds()
    {
        var options = new SlicTransportOptions();
        Assert.That(() => options.PauseWriterThreshold = 0, Throws.Nothing);
        Assert.That(options.PauseWriterThreshold, Is.Zero);
    }

    [Test]
    public void MaxOutstandingPongs_default_value()
    {
        var options = new SlicTransportOptions();
        Assert.That(options.MaxOutstandingPongs, Is.EqualTo(100));
    }

    [Test]
    public void MaxOutstandingPongs_below_minimum_throws()
    {
        var options = new SlicTransportOptions();
        Assert.That(() => options.MaxOutstandingPongs = 0, Throws.TypeOf<ArgumentException>());
    }

    /// <summary>Verifies that <see cref="SlicTransportOptions.PauseWriterThreshold"/> = 0 disables connection-level
    /// flow control: a write can fill the outbound pipe arbitrarily without parking on FlushAsync, even when the
    /// background writer task can't drain the pipe.</summary>
    [Test]
    public async Task Large_write_completes_with_pause_writer_threshold_disabled()
    {
        // Arrange: PauseWriterThreshold = 0 on the client disables the per-connection pipe pause. Hold the duplex
        // Write so the background writer task cannot drain the outbound pipe — with any non-zero threshold the
        // FlushAsync would park; with 0 it must complete. The peer window is sized well above the payload so the
        // write isn't bounded by Slic's per-stream send credit.
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.InitialStreamWindowSize = 256 * 1024);
        services.AddOptions<SlicTransportOptions>("client").Configure(
            options => options.PauseWriterThreshold = 0);
        services.AddTestDuplexTransportDecorator();
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        var duplexClientConnection =
            provider.GetRequiredService<TestDuplexClientTransportDecorator>().LastCreatedConnection;

        using var streams = await sut.CreateAndAcceptStreamAsync();

        duplexClientConnection.Operations.Hold = DuplexTransportOperations.Write;

        // Act: write a payload much larger than the default Pipe pauseWriterThreshold (64 KB) into the held
        // outbound pipe. Stays comfortably within the peer's 256 KB send credit so the write isn't gated by
        // peer-window flow control. The cancellation token bounds the test in case the threshold isn't actually
        // disabled — WriteAsync would otherwise park indefinitely on FlushAsync.
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] payload = new byte[128 * 1024];
        try
        {
            FlushResult flushResult = await streams.Local.Output.WriteAsync(payload, cts.Token);

            // Assert: the write completes without the background writer task draining anything.
            Assert.That(flushResult.IsCompleted, Is.False);
            Assert.That(flushResult.IsCanceled, Is.False);
        }
        finally
        {
            duplexClientConnection.Operations.Hold = DuplexTransportOperations.None;
        }

        // Once the background writer task drains the pipe, all bytes must reach the peer.
        int totalRead = 0;
        while (totalRead < payload.Length)
        {
            ReadResult readResult = await streams.Remote.Input.ReadAtLeastAsync(1, cts.Token);
            totalRead += (int)readResult.Buffer.Length;
            streams.Remote.Input.AdvanceTo(readResult.Buffer.End);
        }
        Assert.That(totalRead, Is.EqualTo(payload.Length));
    }

    /// <summary>Verifies that a write blocked on peer-granted send credit (peer window exhausted) can be
    /// canceled.</summary>
    [Test]
    public async Task Stream_write_cancellation_while_blocked_on_peer_credit()
    {
        // Arrange: use a small peer window so it's easy to exhaust.
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.InitialStreamWindowSize = 4 * 1024);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();
        using var streams = await sut.CreateAndAcceptStreamAsync();

        // Exhaust the peer's window without reading.
        byte[] payload = new byte[(4 * 1024) - 1];
        _ = await streams.Local.Output.WriteAsync(payload, default);

        using var writeCts = new CancellationTokenSource();

        // Act: the next write blocks on AcquireSendCreditAsync because the peer window is exhausted.
        ValueTask<FlushResult> writeTask = streams.Local.Output.WriteAsync(payload, writeCts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(writeTask.IsCompleted, Is.False);
        writeCts.Cancel();

        // Assert
        Assert.That(
            async () => await writeTask,
            Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that a read returning a canceled result with buffered data can be advanced with positions
    /// from the returned buffer, and that reading resumes correctly afterwards.</summary>
    [Test]
    public async Task Canceled_read_with_buffered_data_then_resume_reading([Values] bool useTryRead)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();
        using var streams = await sut.CreateAndAcceptStreamAsync(bidirectional: false);

        // Buffer data on the remote stream input.
        _ = await streams.Local.Output.WriteAsync(new byte[1024], default);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        // Act: cancel the next read; it returns the buffered data with IsCanceled=true.
        streams.Remote.Input.CancelPendingRead();
        ReadResult readResult;
        if (useTryRead)
        {
            Assert.That(streams.Remote.Input.TryRead(out readResult), Is.True);
        }
        else
        {
            readResult = await streams.Remote.Input.ReadAsync(default);
        }
        Assert.That(readResult.IsCanceled, Is.True);
        Assert.That(readResult.Buffer, Has.Length.EqualTo(1024));

        // Examine everything without consuming: the next read won't complete until more data arrives.
        streams.Remote.Input.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.End);

        // Assert: the stream still works. Write the last stream frame; the next read returns all the data.
        _ = await ((ReadOnlySequencePipeWriter)streams.Local.Output).WriteAsync(
            new ReadOnlySequence<byte>(new byte[1]),
            endStream: true,
            default);
        readResult = await streams.Remote.Input.ReadAsync(default).AsTask().WaitAsync(TimeSpan.FromMinutes(2));
        Assert.That(readResult.IsCanceled, Is.False);
        Assert.That(readResult.IsCompleted, Is.True);
        Assert.That(readResult.Buffer, Has.Length.EqualTo(1025));
        streams.Remote.Input.AdvanceTo(readResult.Buffer.End);
    }

    /// <summary>Verifies that AdvanceTo following a canceled read computes the window accounting against the buffer
    /// returned by the canceled read: the total of the StreamWindowUpdate increments sent to the peer never exceeds
    /// the number of bytes the peer wrote on the stream.</summary>
    [Test]
    public async Task Canceled_read_advance_does_not_corrupt_window_accounting()
    {
        // Arrange: small server-side stream window so window updates are sent frequently (the window update
        // threshold is half the window size, 2048 bytes here).
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.InitialStreamWindowSize = 4 * 1024);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        Task connectTask = duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var _ = multiplexedServerConnection;
        await connectTask;
        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);

        // Connect the multiplexed connection.
        await WriteInitializeFrameAsync(duplexClientConnection, version: 1);
        await multiplexedServerConnection.ConnectAsync(default);
        await ReadFrameAsync(reader);

        // Open a unidirectional stream (the first client unidirectional stream ID is 2) with 4096 bytes and consume
        // them on the server; this exercises the regular window accounting (one or more window updates).
        ValueTask<IMultiplexedStream> acceptStreamTask = multiplexedServerConnection.AcceptStreamAsync(default);
        await WriteRawStreamFrameAsync(duplexClientConnection, streamId: 2ul, size: 4096);
        IMultiplexedStream serverStream = await acceptStreamTask;
        await ReadStreamDataAsync(serverStream.Input, 4096);

        // Write 1000 additional bytes and give the server time to buffer them on the stream input.
        await WriteRawStreamFrameAsync(duplexClientConnection, streamId: 2ul, size: 1000);
        await Task.Delay(TimeSpan.FromMilliseconds(100));

        // Act: cancel the next read; it returns the buffered data with IsCanceled=true. Advance without consuming
        // or examining anything.
        serverStream.Input.CancelPendingRead();
        ReadResult readResult = await serverStream.Input.ReadAsync(default);
        Assert.That(readResult.IsCanceled, Is.True);
        Assert.That(readResult.Buffer, Has.Length.EqualTo(1000));
        serverStream.Input.AdvanceTo(readResult.Buffer.Start, readResult.Buffer.Start);

        // Resume reading: consume the 1000 buffered bytes, then 1100 more.
        await ReadStreamDataAsync(serverStream.Input, 1000);
        await WriteRawStreamFrameAsync(duplexClientConnection, streamId: 2ul, size: 1100);
        await ReadStreamDataAsync(serverStream.Input, 1100);

        // Completing the input sends the StreamReadsClosed frame, which bounds the frame collection below.
        serverStream.Input.Complete();

        // Assert: the sum of the window update increments can't exceed the bytes written on the stream.
        long windowUpdateTotal = 0;
        while (true)
        {
            (FrameType frameType, ReadOnlySequence<byte> buffer) =
                await ReadFrameAsync(reader).WaitAsync(TimeSpan.FromMinutes(2));
            if (frameType == FrameType.StreamWindowUpdate)
            {
                var decoder = new SliceDecoder(buffer);
                Assert.That(decoder.DecodeVarUInt62(), Is.EqualTo(2ul)); // stream ID
                windowUpdateTotal += (long)new StreamWindowUpdateBody(ref decoder).WindowSizeIncrement;
            }
            else if (frameType == FrameType.StreamReadsClosed)
            {
                break;
            }
        }
        Assert.That(windowUpdateTotal, Is.LessThanOrEqualTo(4096 + 1000 + 1100));

        static async Task ReadStreamDataAsync(PipeReader input, long byteCount)
        {
            long read = 0;
            while (read < byteCount)
            {
                ReadResult readResult = await input.ReadAsync(default).AsTask().WaitAsync(TimeSpan.FromMinutes(2));
                read += readResult.Buffer.Length;
                input.AdvanceTo(readResult.Buffer.End, readResult.Buffer.End);
            }
        }

        // Like WriteStreamFrameAsync but encodes the frame into a single fixed-size buffer.
        static Task WriteRawStreamFrameAsync(IDuplexConnection connection, ulong streamId, int size)
        {
            // size + headroom for the frame header that precedes the body: 1-byte frame type + 4-byte size
            // placeholder + up to 8-byte stream ID (varuint62) = at most 13 bytes; 16 is a safe round bound.
            var writer = new MemoryBufferWriter(new byte[size + 16]);
            var encoder = new SliceEncoder(writer);
            encoder.EncodeFrameType(FrameType.Stream);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encoder.EncodeVarUInt62(streamId);
            encoder.WriteByteSpan(new byte[size]);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
            return connection.WriteAsync(new ReadOnlySequence<byte>(writer.WrittenMemory), default).AsTask();
        }
    }

    /// <summary>Verifies that a write blocked on the connection-level pipe pause (PauseWriterThreshold reached)
    /// can be canceled.</summary>
    [Test]
    public async Task Stream_write_cancellation_while_blocked_on_pipe_threshold()
    {
        // Arrange: use a small PauseWriterThreshold and hold the duplex Write so the connection-level outbound
        // pipe fills up and FlushAsync parks.
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.InitialStreamWindowSize = 128 * 1024);
        services.AddOptions<SlicTransportOptions>("client").Configure(
            options => options.PauseWriterThreshold = 4 * 1024);
        services.AddTestDuplexTransportDecorator();
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        var duplexClientConnection =
            provider.GetRequiredService<TestDuplexClientTransportDecorator>().LastCreatedConnection;

        using var streams = await sut.CreateAndAcceptStreamAsync();

        // Hold duplex writes so the background writer task can't drain the connection-level pipe.
        duplexClientConnection.Operations.Hold = DuplexTransportOperations.Write;

        using var writeCts = new CancellationTokenSource();

        try
        {
            // Act: this write fills the connection-level pipe past PauseWriterThreshold and parks on FlushAsync.
            ValueTask<FlushResult> writeTask = streams.Local.Output.WriteAsync(
                new byte[8 * 1024],
                writeCts.Token);
            await Task.Delay(TimeSpan.FromMilliseconds(50));
            Assert.That(writeTask.IsCompleted, Is.False);
            writeCts.Cancel();

            // Assert
            Assert.That(
                async () => await writeTask,
                Throws.InstanceOf<OperationCanceledException>());
        }
        finally
        {
            duplexClientConnection.Operations.Hold = DuplexTransportOperations.None;
        }
    }

    /// <summary>Verifies that the connection remains usable after a write parked on the connection-level pipe pause
    /// is canceled. The cancellation cancels the FlushAsync wait (and completes the writer side of the affected
    /// stream), but the underlying connection is not aborted — a new stream on the same connection can still be
    /// created and written to.</summary>
    [Test]
    public async Task Connection_remains_usable_after_canceled_pipe_threshold_write()
    {
        // Arrange: same configuration as Stream_write_cancellation_while_blocked_on_pipe_threshold.
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.InitialStreamWindowSize = 128 * 1024);
        services.AddOptions<SlicTransportOptions>("client").Configure(
            options => options.PauseWriterThreshold = 4 * 1024);
        services.AddTestDuplexTransportDecorator();
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();

        var duplexClientConnection =
            provider.GetRequiredService<TestDuplexClientTransportDecorator>().LastCreatedConnection;

        using var firstStreams = await sut.CreateAndAcceptStreamAsync();

        // Hold duplex writes so the background writer task can't drain the connection-level pipe.
        duplexClientConnection.Operations.Hold = DuplexTransportOperations.Write;

        using var writeCts = new CancellationTokenSource();

        ValueTask<FlushResult> firstWriteTask = firstStreams.Local.Output.WriteAsync(
            new byte[8 * 1024],
            writeCts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(firstWriteTask.IsCompleted, Is.False);
        writeCts.Cancel();

        Assert.That(async () => await firstWriteTask, Throws.InstanceOf<OperationCanceledException>());

        // Act: release the duplex hold so the background writer task can drain, then open a new stream and write on
        // it. This proves the connection itself is unaffected by the cancellation.
        duplexClientConnection.Operations.Hold = DuplexTransportOperations.None;

        using var secondStreams = await sut.CreateAndAcceptStreamAsync();
        byte[] payload = new byte[1024];
        FlushResult flushResult = await secondStreams.Local.Output.WriteAsync(payload);

        // Assert: the new stream's write completes normally.
        Assert.That(flushResult.IsCompleted, Is.False);
        Assert.That(flushResult.IsCanceled, Is.False);

        ReadResult readResult = await secondStreams.Remote.Input.ReadAtLeastAsync(payload.Length);
        Assert.That((int)readResult.Buffer.Length, Is.GreaterThanOrEqualTo(payload.Length));
        secondStreams.Remote.Input.AdvanceTo(readResult.Buffer.End);
    }

    /// <summary>Verifies that a server-side dispose does not deadlock when the outbound pipe is paused
    /// (PauseWriterThreshold reached) and a concurrent server-side <see cref="IMultiplexedConnection.CloseAsync"/> is
    /// parked on FlushAsync while holding the connection's write semaphore.</summary>
    [Test]
    public async Task Server_dispose_does_not_deadlock_when_close_is_blocked_on_pipe_threshold()
    {
        // Arrange: small server-side PauseWriterThreshold. We apply the duplex Write hold *after* the handshake, so
        // the connection establishes normally and only subsequent server-side writes accumulate in the pipe.
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(options =>
        {
            options.PauseWriterThreshold = 1024;
            options.InitialStreamWindowSize = 128 * 1024;
        });
        services.AddOptions<SlicTransportOptions>("client").Configure(
            options => options.InitialStreamWindowSize = 128 * 1024);
        services.AddTestDuplexTransportDecorator();
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();
        using var streams = await sut.CreateAndAcceptStreamAsync();

        var serverDuplexConnection =
            provider.GetRequiredService<TestDuplexServerTransportDecorator>().LastAcceptedConnection;
        serverDuplexConnection.Operations.Hold = DuplexTransportOperations.Write;

        // Push enough server-side stream data into the outbound pipe to exceed PauseWriterThreshold. This write parks
        // on FlushAsync; CloseAsync below cancels _closedCts which cancels the write's writeCts and releases the write
        // semaphore — but the bytes remain buffered, leaving the pipe past PauseWriterThreshold.
        Task<FlushResult> serverWriteTask = streams.Remote.Output.WriteAsync(new byte[4 * 1024], default).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(serverWriteTask.IsCompleted, Is.False);

        // Act: server.CloseAsync(None) writes the Close frame and parks on FlushAsync(None) while holding
        // _writeSemaphore. server.DisposeAsync waits for _writeSemaphore.
        Task closeTask = sut.Server.CloseAsync(MultiplexedConnectionCloseError.NoError, default);
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(closeTask.IsCompleted, Is.False);

        // Assert: DisposeAsync must complete.
        Task disposeTask = sut.Server.DisposeAsync().AsTask();
        Assert.That(async () => await disposeTask.WaitAsync(TimeSpan.FromSeconds(5)), Throws.Nothing);

        // Tidy up the in-flight tasks. The duplex hold is released by the server connection dispose above.
        try
        {
            await closeTask;
        }
        catch
        {
        }
        try
        {
            await serverWriteTask;
        }
        catch
        {
        }
    }

    /// <summary>Verifies that the read frames loop is not blocked by the Pong frame write that responds to a Ping
    /// frame when another write holds the connection's write semaphore while parked on the connection-level pipe
    /// pause (PauseWriterThreshold reached).</summary>
    [Test]
    public async Task Read_frames_loop_is_not_blocked_by_pong_write_when_a_write_is_parked_on_pipe_threshold()
    {
        // Arrange: small server-side PauseWriterThreshold so a server stream write fills the connection-level
        // outbound pipe and parks on FlushAsync while holding the write semaphore.
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(options => options.PauseWriterThreshold = 1024);
        services.AddTestDuplexTransportDecorator();
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        Task connectTask = duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var _ = multiplexedServerConnection;
        await connectTask;
        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);

        // Connect the multiplexed connection.
        await WriteInitializeFrameAsync(duplexClientConnection, version: 1);
        await multiplexedServerConnection.ConnectAsync(default);
        await ReadFrameAsync(reader);

        // Open a bidirectional stream (the first client bidirectional stream ID is 0) and accept it on the server.
        ValueTask<IMultiplexedStream> acceptStreamTask = multiplexedServerConnection.AcceptStreamAsync(default);
        await WriteStreamFrameAsync(
            duplexClientConnection,
            FrameType.Stream,
            streamId: 0ul,
            (ref SliceEncoder encoder) => encoder.EncodeBool(false));
        IMultiplexedStream serverStream = await acceptStreamTask;

        // Hold server duplex writes so the background writer task can't drain the connection-level outbound pipe.
        var serverDuplexConnection =
            provider.GetRequiredService<TestDuplexServerTransportDecorator>().LastAcceptedConnection;
        serverDuplexConnection.Operations.Hold = DuplexTransportOperations.Write;

        // This write fills the outbound pipe past PauseWriterThreshold and parks on FlushAsync while holding the
        // write semaphore.
        Task<FlushResult> serverWriteTask = serverStream.Output.WriteAsync(new byte[4 * 1024], default).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(serverWriteTask.IsCompleted, Is.False);

        // Act: the Pong frame write that responds to this Ping frame can't make progress while the write semaphore
        // is held.
        await WriteFrameAsync(duplexClientConnection, FrameType.Ping, new PingBody(0L).Encode);

        // Open a second stream (the next client bidirectional stream ID is 4) to verify that the read frames loop
        // still processes incoming frames.
        ValueTask<IMultiplexedStream> nextAcceptStreamTask = multiplexedServerConnection.AcceptStreamAsync(default);
        await WriteStreamFrameAsync(
            duplexClientConnection,
            FrameType.Stream,
            streamId: 4ul,
            (ref SliceEncoder encoder) => encoder.EncodeBool(false));

        // Assert. The WaitAsync margin exceeds CI's --blame-hang-timeout (60 s) so that a hang is detected and
        // dumped by the hang detection first; the timeout is only a fallback that fails this test instead of
        // hanging the test run when hang detection is not enabled.
        Assert.That(
            async () => await nextAcceptStreamTask.AsTask().WaitAsync(TimeSpan.FromMinutes(2)),
            Throws.Nothing);

        // The parked write is still blocked while the read frames loop accepted the second stream above: reads
        // make progress independently of the stalled write (and its still-queued pong).
        Assert.That(serverWriteTask.IsCompleted, Is.False);

        // Cleanup: release the hold so the parked write (and the queued pong write) can complete.
        serverDuplexConnection.Operations.Hold = DuplexTransportOperations.None;
        Assert.That(async () => await serverWriteTask, Throws.Nothing);
    }

    /// <summary>Verifies that the connection is aborted when a Ping frame is received while
    /// <see cref="SlicTransportOptions.MaxOutstandingPongs" /> Pong frames are already queued for sending. See
    /// icerpc/icerpc-csharp-audit#17.</summary>
    [Test]
    public async Task Connection_is_aborted_when_max_outstanding_pongs_is_exceeded()
    {
        // Arrange: same parked-write arrangement as the previous test, plus a small MaxOutstandingPongs.
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(options =>
        {
            options.MaxOutstandingPongs = 3;
            options.PauseWriterThreshold = 1024;
        });
        services.AddTestDuplexTransportDecorator();
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        Task connectTask = duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var _ = multiplexedServerConnection;
        await connectTask;
        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);

        // Connect the multiplexed connection.
        await WriteInitializeFrameAsync(duplexClientConnection, version: 1);
        await multiplexedServerConnection.ConnectAsync(default);
        await ReadFrameAsync(reader);

        // Open a bidirectional stream (the first client bidirectional stream ID is 0) and accept it on the server.
        ValueTask<IMultiplexedStream> acceptStreamTask = multiplexedServerConnection.AcceptStreamAsync(default);
        await WriteStreamFrameAsync(
            duplexClientConnection,
            FrameType.Stream,
            streamId: 0ul,
            (ref SliceEncoder encoder) => encoder.EncodeBool(false));
        IMultiplexedStream serverStream = await acceptStreamTask;

        // Hold server duplex writes so the background writer task can't drain the connection-level outbound pipe.
        var serverDuplexConnection =
            provider.GetRequiredService<TestDuplexServerTransportDecorator>().LastAcceptedConnection;
        serverDuplexConnection.Operations.Hold = DuplexTransportOperations.Write;

        // This write fills the outbound pipe past PauseWriterThreshold and parks on FlushAsync while holding the
        // write semaphore.
        Task<FlushResult> serverWriteTask = serverStream.Output.WriteAsync(new byte[4 * 1024], default).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(serverWriteTask.IsCompleted, Is.False);

        ValueTask<IMultiplexedStream> nextAcceptStreamTask = multiplexedServerConnection.AcceptStreamAsync(default);

        // Act: each Ping frame queues a Pong frame write that can't make progress while the write semaphore is
        // held; the fourth Ping frame exceeds MaxOutstandingPongs.
        for (int i = 0; i < 4; ++i)
        {
            await WriteFrameAsync(duplexClientConnection, FrameType.Ping, new PingBody(0L).Encode);
        }

        // Assert. The WaitAsync margin exceeds CI's --blame-hang-timeout (60 s) so that a hang is detected and
        // dumped by the hang detection first; the timeout is only a fallback that fails this test instead of
        // hanging the test run when hang detection is not enabled.
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(
            async () => await nextAcceptStreamTask.AsTask().WaitAsync(TimeSpan.FromMinutes(2)));
        Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.IceRpcError));
        Assert.That(
            exception.Message,
            Is.EqualTo("Received a Ping frame while 3 Pong frames are already queued for sending."));

        // Cleanup: the connection abort unblocks the parked write with an exception.
        try
        {
            await serverWriteTask;
        }
        catch
        {
        }
    }

    /// <summary>Verifies that a Ping frame received after a local CloseAsync doesn't fault the read frames loop:
    /// the read frames loop keeps reading until the peer shuts down the duplex connection, and the Pong write
    /// failure on the closed connection must be ignored. See icerpc/icerpc-csharp-audit#29.</summary>
    [Test]
    public async Task Ping_received_after_local_close_does_not_fail_graceful_close()
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddSlicTest()
            .BuildServiceProvider(validateScopes: true);

        var duplexClientTransport = provider.GetRequiredService<IDuplexClientTransport>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var acceptTask = listener.AcceptAsync(default);
        using var duplexClientConnection = duplexClientTransport.CreateConnection(
            listener.TransportAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        Task connectTask = duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var _ = multiplexedServerConnection;
        await connectTask;
        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);

        // Connect the multiplexed connection.
        await WriteInitializeFrameAsync(duplexClientConnection, version: 1);
        await multiplexedServerConnection.ConnectAsync(default);
        await ReadFrameAsync(reader);

        // Close the server connection; the read frames loop keeps reading until the client shuts down the duplex
        // connection.
        Task closeTask = multiplexedServerConnection.CloseAsync(MultiplexedConnectionCloseError.NoError, default);

        // Wait for the Close frame.
        (FrameType frameType, ReadOnlySequence<byte> buffer) = await ReadFrameAsync(reader)
            .WaitAsync(TimeSpan.FromMinutes(2));
        Assert.That(frameType, Is.EqualTo(FrameType.Close));

        // Act: send a Ping frame on the closed connection, then shut down the duplex connection.
        await WriteFrameAsync(duplexClientConnection, FrameType.Ping, new PingBody(0L).Encode);
        await duplexClientConnection.ShutdownWriteAsync(default);

        // Assert: the graceful close completes successfully. The WaitAsync margin exceeds CI's --blame-hang-timeout
        // (60 s) so that a hang is detected and dumped by the hang detection first; the timeout is only a fallback
        // that fails this test instead of hanging the test run when hang detection is not enabled.
        Assert.That(async () => await closeTask.WaitAsync(TimeSpan.FromMinutes(2)), Throws.Nothing);
    }

    [Test]
    public async Task Large_write_completes_with_small_pause_writer_threshold()
    {
        // Arrange: peer advertises a large window (256 KB) but local PauseWriterThreshold is small (8 KB).
        // The write of 256 KB must be chunked through the PauseWriterThreshold without deadlocking.
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.InitialStreamWindowSize = 256 * 1024);
        services.AddOptions<SlicTransportOptions>("client").Configure(
            options => options.PauseWriterThreshold = 8 * 1024);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();
        using var streams = await sut.CreateAndAcceptStreamAsync();

        // Act: write a payload much larger than PauseWriterThreshold.
        byte[] payload = new byte[256 * 1024];
        ValueTask<FlushResult> writeTask = streams.Local.Output.WriteAsync(payload, default);

        // The peer reads everything.
        int totalRead = 0;
        while (totalRead < payload.Length)
        {
            ReadResult readResult = await streams.Remote.Input.ReadAtLeastAsync(1);
            totalRead += (int)readResult.Buffer.Length;
            streams.Remote.Input.AdvanceTo(readResult.Buffer.End);
        }

        // Assert: the write completes without deadlock and all bytes are received.
        await writeTask;
        Assert.That(totalRead, Is.EqualTo(payload.Length));
    }

    [TestCase(1024)]
    [TestCase(4 * 1024)]
    [TestCase(64 * 1024)]
    public async Task Write_completes_with_various_pause_writer_thresholds(int threshold)
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.InitialStreamWindowSize = 128 * 1024);
        services.AddOptions<SlicTransportOptions>("client").Configure(
            options => options.PauseWriterThreshold = threshold);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();
        using var streams = await sut.CreateAndAcceptStreamAsync();

        // Act: write a payload larger than PauseWriterThreshold.
        byte[] payload = new byte[64 * 1024];
        ValueTask<FlushResult> writeTask = streams.Local.Output.WriteAsync(payload, default);

        int totalRead = 0;
        while (totalRead < payload.Length)
        {
            ReadResult readResult = await streams.Remote.Input.ReadAtLeastAsync(1);
            totalRead += (int)readResult.Buffer.Length;
            streams.Remote.Input.AdvanceTo(readResult.Buffer.End);
        }

        // Assert
        await writeTask;
        Assert.That(totalRead, Is.EqualTo(payload.Length));
    }

    [Test]
    public async Task Concurrent_streams_with_small_pause_writer_threshold()
    {
        // Arrange
        IServiceCollection services = new ServiceCollection().AddSlicTest();
        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.InitialStreamWindowSize = 128 * 1024);
        services.AddOptions<SlicTransportOptions>("client").Configure(
            options => options.PauseWriterThreshold = 8 * 1024);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerMultiplexedConnection>();
        await sut.AcceptAndConnectAsync();
        using var streams1 = await sut.CreateAndAcceptStreamAsync();
        using var streams2 = await sut.CreateAndAcceptStreamAsync();

        // Act: both streams write concurrently.
        byte[] payload = new byte[32 * 1024];

        ValueTask<FlushResult> writeTask1 = streams1.Local.Output.WriteAsync(payload, default);
        ValueTask<FlushResult> writeTask2 = streams2.Local.Output.WriteAsync(payload, default);

        // Read from both streams concurrently.
        async Task ReadAllAsync(PipeReader reader, int expectedBytes)
        {
            int totalRead = 0;
            while (totalRead < expectedBytes)
            {
                ReadResult readResult = await reader.ReadAtLeastAsync(1);
                totalRead += (int)readResult.Buffer.Length;
                reader.AdvanceTo(readResult.Buffer.End);
            }
        }

        await Task.WhenAll(
            ReadAllAsync(streams1.Remote.Input, payload.Length),
            ReadAllAsync(streams2.Remote.Input, payload.Length));

        // Assert: both writes complete.
        await writeTask1;
        await writeTask2;
    }

    private static async Task<(FrameType FrameType, ReadOnlySequence<byte> Buffer)> ReadFrameAsync(
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

                // The buffer can hold more than one frame; only copy and consume this frame's bytes so that
                // back-to-back frames can be read with successive calls.
                Memory<byte> frameBuffer = new byte[header.FrameSize];
                buffer.Slice(0, header.FrameSize).CopyTo(frameBuffer.Span);
                reader.AdvanceTo(buffer.GetPosition(header.FrameSize));
                return (header.FrameType, new ReadOnlySequence<byte>(frameBuffer));
            }
            else
            {
                reader.AdvanceTo(buffer.Start, buffer.End);
            }
        }

        static bool TryDecodeHeader(
            ReadOnlySequence<byte> buffer,
            out (FrameType FrameType, int FrameSize) header,
            out int consumed)
        {
            header = default;
            consumed = default;

            var decoder = new SliceDecoder(buffer);

            // Decode the frame type and frame size.
            if (!decoder.TryDecodeUInt8(out byte frameType) || !decoder.TryDecodeVarUInt62(out ulong frameSize))
            {
                return false;
            }
            header.FrameType = frameType.AsFrameType();
            header.FrameSize = checked((int)frameSize);
            consumed = (int)decoder.Consumed;
            return true;
        }
    }

    private static Task WriteFrameAsync(
        IDuplexConnection connection,
        FrameType frameType,
        EncodeAction? encodeAction = null)
    {
        var writer = new MemoryBufferWriter(new byte[1024]);
        Encode(writer);
        return connection.WriteAsync(new ReadOnlySequence<byte>(writer.WrittenMemory), default).AsTask();

        void Encode(IBufferWriter<byte> writer)
        {
            var encoder = new SliceEncoder(writer);
            encoder.EncodeFrameType(frameType);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction?.Invoke(ref encoder);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
        }
    }

    /// <summary>Writes a frame with a body of the specified size. Used to test oversized frame rejection.</summary>
    private static Task WriteOversizedFrameAsync(IDuplexConnection connection, FrameType frameType, int bodySize)
    {
        var writer = new MemoryBufferWriter(new byte[bodySize + 5]); // header takes 5 bytes
        Encode(writer);
        return connection.WriteAsync(new ReadOnlySequence<byte>(writer.WrittenMemory), default).AsTask();

        void Encode(IBufferWriter<byte> writer)
        {
            var encoder = new SliceEncoder(writer);
            encoder.EncodeFrameType(frameType);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encoder.WriteByteSpan(new byte[bodySize]);
            SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);
        }
    }

    private static Task WriteInitializeFrameAsync(IDuplexConnection connection, ulong version)
    {
        return WriteFrameAsync(connection, FrameType.Initialize, Encode);

        void Encode(ref SliceEncoder encoder)
        {
            // Required parameters.
            var parameters = new Dictionary<ParameterKey, IList<byte>>();
            byte[] packetMaxSizeBuffer = new byte[4];
            SliceEncoder.EncodeVarUInt62(4096, packetMaxSizeBuffer);
            parameters[ParameterKey.MaxStreamFrameSize] = packetMaxSizeBuffer;
            byte[] initialStreamWindowSizeBuffer = new byte[4];
            SliceEncoder.EncodeVarUInt62(32 * 1024, initialStreamWindowSizeBuffer);
            parameters[ParameterKey.InitialStreamWindowSize] = initialStreamWindowSizeBuffer;

            var initializeBody = new InitializeBody(parameters);
            encoder.EncodeVarUInt62(version);
            initializeBody.Encode(ref encoder);
        }
    }

    private static Task WriteStreamFrameAsync(
        IDuplexConnection connection,
        FrameType frameType,
        ulong streamId,
        EncodeAction? encodeAction = null) =>
        WriteFrameAsync(
            connection,
            frameType,
            (ref SliceEncoder encoder) =>
            {
                encoder.EncodeVarUInt62(streamId);
                encodeAction?.Invoke(ref encoder);
            });
}
