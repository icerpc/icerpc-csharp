// Copyright (c) ZeroC, Inc.

using IceRpc.Internal;
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
using ZeroC.Slice;

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
            foreach (FrameType frameType in new FrameType[] {
                    FrameType.Initialize,
                    FrameType.InitializeAck,
                    FrameType.Version
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
            foreach (FrameType frameType in new FrameType[] {
                    FrameType.StreamReadsClosed,
                    FrameType.StreamWindowUpdate,
                    FrameType.StreamWritesClosed
                 })
            {
                yield return new TestCaseData(
                    (IDuplexConnection connection) => WriteStreamFrameAsync(connection, frameType, streamId: 4ul),
                    $"Received {frameType} frame for unknown stream.");
            }

            // Bogus stream frame with FrameSize=0 (stream ID not encoded)
            yield return new TestCaseData((IDuplexConnection connection) =>
                WriteFrameAsync(connection, FrameType.StreamLast, (ref SliceEncoder encoder) => { }),
                "Invalid stream frame size.");

            // Encode a stream frame with a frame size inferior to the stream ID size.
            yield return new TestCaseData((IDuplexConnection connection) =>
                {
                    var writer = new MemoryBufferWriter(new byte[1024]);
                    {
                        var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
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
            listener.ServerAddress,
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
            new ServerAddress(new Uri("icerpc://[::1]")),
            options: new(),
            serverAuthenticationOptions: null);
        var acceptTask = listener.AcceptAsync(default);
        await using var multiplexedClientConnection = multiplexedClientTransport.CreateConnection(
            listener.ServerAddress,
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
            new ServerAddress(new Uri("icerpc://[::1]")),
            options: new(),
            serverAuthenticationOptions: null);
        var acceptTask = listener.AcceptAsync(default);
        await using var multiplexedClientConnection = multiplexedClientTransport.CreateConnection(
            listener.ServerAddress,
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
            listener.ServerAddress,
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
            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);
            var versionBody = new VersionBody(ref decoder);
            decoder.CheckEndOfBuffer();
            return versionBody;
        }
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
    public async Task Connection_with_idle_timeout_is_not_aborted_when_idle([Values] bool serverIdleTimeout)
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

    /// <summary>Verifies that setting the idle timeout doesn't abort the connection when there is slow write activity
    /// from client to server.</summary>
    [Test]
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
        Assert.That(nextAcceptStreamTask.IsCompleted, Is.False);
        await sut.Client.CloseAsync(MultiplexedConnectionCloseError.NoError, default);
        Assert.That(async () => await nextAcceptStreamTask, Throws.InstanceOf<IceRpcException>());
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

    [Test, TestCaseSource(nameof(ConnectionProtocolErrors))]
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
            listener.ServerAddress,
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
            listener.ServerAddress,
            new DuplexConnectionOptions(),
            clientAuthenticationOptions: null);
        Task connectTask = duplexClientConnection.ConnectAsync(default);
        (var multiplexedServerConnection, var transportConnectionInformation) = await acceptTask;
        await using var _ = multiplexedServerConnection;
        await connectTask;
        using var reader = new DuplexConnectionReader(duplexClientConnection, MemoryPool<byte>.Shared, 4096);

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
            new ServerAddress(new Uri("icerpc://[::1]")),
            options: new(),
            serverAuthenticationOptions: null);
        var acceptTask = listener.AcceptAsync(default);
        await using var multiplexedClientConnection = multiplexedClientTransport.CreateConnection(
            listener.ServerAddress,
            new MultiplexedConnectionOptions(),
            clientAuthenticationOptions: null);
        var connectTask = multiplexedClientConnection.ConnectAsync(default);
        (var duplexServerConnection, var transportConnectionInformation) = await acceptTask;
        using var _ = duplexServerConnection;
        using var reader = new DuplexConnectionReader(duplexServerConnection, MemoryPool<byte>.Shared, 4096);

        // Act
        await WriteFrameAsync(duplexServerConnection, frameType, (ref SliceEncoder encoder) => { });

        // Assert
        Assert.That(
            async () => await connectTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.IceRpcError));
    }

    [Test, TestCaseSource(nameof(InvalidConnectionFrames))]
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
            listener.ServerAddress,
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
            listener.ServerAddress,
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

    private static async Task<(FrameType FrameType, ReadOnlySequence<byte> buffer)> ReadFrameAsync(
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
                Memory<byte> frameBuffer = new byte[header.FrameSize];
                buffer.CopyTo(frameBuffer.Span);
                reader.AdvanceTo(buffer.End);
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

            var decoder = new SliceDecoder(buffer, SliceEncoding.Slice2);

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
            var encoder = new SliceEncoder(writer, SliceEncoding.Slice2);
            encoder.EncodeFrameType(frameType);
            Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
            int startPos = encoder.EncodedByteCount;
            encodeAction?.Invoke(ref encoder);
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
