// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;
using System.Net.Security;

namespace IceRpc.Conformance.Tests;

/// <summary>Conformance tests for the multiplexed transports.</summary>
public abstract class MultiplexedConnectionConformanceTests
{
    private static readonly ReadOnlyMemory<byte> _oneBytePayload = new(new byte[] { 0xFF });

    /// <summary>Verifies that both peers can initiate and accept streams.</summary>
    /// <param name="serverInitiated">Whether the stream is initiated by the server or by the client.</param>
    [Test]
    public async Task Accept_a_stream([Values(true, false)] bool serverInitiated)
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);

        IMultiplexedConnection clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        using LocalAndRemoteStreams sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            serverInitiated ? serverConnection : clientConnection,
            serverInitiated ? clientConnection : serverConnection);

        Assert.That(sut.LocalStream.Id, Is.EqualTo(sut.RemoteStream.Id));
    }

    /// <summary>Verifies that accept stream calls can be canceled.</summary>
    [Test]
    public async Task Accept_stream_cancellation()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        using var cts = new CancellationTokenSource();
        ValueTask<IMultiplexedStream> acceptTask = serverConnection.AcceptStreamAsync(cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(10)); // give a few ms for acceptTask to start

        // Act
        cts.Cancel();

        // Assert
        Assert.That(async () => await acceptTask, Throws.TypeOf<OperationCanceledException>());

        // We also verify we can still create new streams. This shows that canceling AcceptAsync does not "abort" new
        // streams and is a transient cancellation (not obvious with QUIC).
        Assert.That(
            async () =>
            {
                using var streams = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
                    clientConnection,
                    serverConnection);
            },
            Throws.Nothing);
    }

    /// <summary>Verifies that AcceptStream fails when the connection is closed.</summary>
    [TestCase(MultiplexedConnectionCloseError.NoError, IceRpcError.ConnectionClosedByPeer)]
    [TestCase(MultiplexedConnectionCloseError.Aborted, IceRpcError.ConnectionAborted)]
    [TestCase(MultiplexedConnectionCloseError.Refused, IceRpcError.ConnectionRefused)]
    [TestCase(MultiplexedConnectionCloseError.ServerBusy, IceRpcError.ServerBusy)]
    [TestCase((MultiplexedConnectionCloseError)255, IceRpcError.ConnectionAborted)]
    public async Task Accept_stream_fails_on_close(
        MultiplexedConnectionCloseError closeError,
        IceRpcError expectedIceRpcError)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        IMultiplexedConnection clientConnection =
            provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        Task acceptStreamTask = serverConnection.AcceptStreamAsync(CancellationToken.None).AsTask();

        // Act
        await clientConnection.CloseAsync(closeError, CancellationToken.None);

        // Assert
        IceRpcException ex = Assert.ThrowsAsync<IceRpcException>(async () => await acceptStreamTask)!;
        Assert.That(ex.IceRpcError, Is.EqualTo(expectedIceRpcError));
    }

    [Test]
    public async Task Completing_a_local_or_remote_stream_allows_accepting_a_new_one(
        [Values(true, false)] bool bidirectional,
        [Values(true, false)] bool abort,
        [Values(true, false)] bool remote)
    {
        // Arrange
        IServiceCollection serviceCollection = CreateServiceCollection().AddMultiplexedTransportTest();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
            bidirectional ?
                options => options.MaxBidirectionalStreams = 1 :
                options => options.MaxUnidirectionalStreams = 1);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        IMultiplexedStream clientStream1 = await clientConnection.CreateStreamAsync(bidirectional, default);
        await clientStream1.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream serverStream1 = await serverConnection.AcceptStreamAsync(default);
        ReadResult readResult = await serverStream1.Input.ReadAsync();
        serverStream1.Input.AdvanceTo(readResult.Buffer.End);

        ValueTask<IMultiplexedStream> clientStream2Task = clientConnection.CreateStreamAsync(bidirectional, default);
        if (bidirectional)
        {
            serverStream1.Output.Complete();
            clientStream1.Input.Complete();
        }
        await Task.Delay(50);

        // Act
        bool stream2TaskIsCompleted = clientStream2Task.IsCompleted;
        if (remote)
        {
            serverStream1.Input.Complete(abort ? new Exception() : null);
        }
        else
        {
            clientStream1.Output.Complete(abort ? new Exception() : null);

            if (!abort)
            {
                // Consume the last stream frame. This will trigger the serverStream1 reads completion and allow the
                // second stream to be accepted.
                readResult = await serverStream1.Input.ReadAsync();
                serverStream1.Input.AdvanceTo(readResult.Buffer.End);
            }
        }

        // Assert

        // The second stream wasn't accepted before the first stream completion.
        Assert.That(stream2TaskIsCompleted, Is.False);

        // The second stream is accepted after the first stream completion.
        Assert.That(async () => await clientStream2Task, Throws.Nothing);

        MultiplexedConformanceTestsHelper.CleanupStreams(clientStream1, serverStream1);
        MultiplexedConformanceTestsHelper.CleanupStreams(await clientStream2Task);
    }

    [Test]
    public async Task Cancel_create_stream_which_is_waiting_on_streams_semaphore()
    {
        // Arrange
        IServiceCollection serviceCollection = CreateServiceCollection().AddMultiplexedTransportTest();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxBidirectionalStreams = 1);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        IMultiplexedStream clientStream1 = await clientConnection.CreateStreamAsync(bidirectional: true, default);
        await clientStream1.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream serverStream1 = await serverConnection.AcceptStreamAsync(default);
        ReadResult readResult = await serverStream1.Input.ReadAsync();
        serverStream1.Input.AdvanceTo(readResult.Buffer.End);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act/Assert
        Assert.That(
            async () => await clientConnection.CreateStreamAsync(bidirectional: true, cts.Token),
            Throws.TypeOf<OperationCanceledException>());

        MultiplexedConformanceTestsHelper.CleanupStreams(clientStream1, serverStream1);
    }

    [Test]
    public async Task Create_stream_which_is_waiting_on_streams_semaphore_fails_when_connection_is_closed()
    {
        // Arrange
        IServiceCollection serviceCollection = CreateServiceCollection().AddMultiplexedTransportTest();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxBidirectionalStreams = 1);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        IMultiplexedStream clientStream1 = await clientConnection.CreateStreamAsync(bidirectional: true, default);
        await clientStream1.Output.WriteAsync(_oneBytePayload, default);

        IMultiplexedStream serverStream1 = await serverConnection.AcceptStreamAsync(default);
        ReadResult readResult = await serverStream1.Input.ReadAsync();
        serverStream1.Input.AdvanceTo(readResult.Buffer.End);

        // Act/Assert
        var createStreamTask = clientConnection.CreateStreamAsync(bidirectional: true, default).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(50));
        Assert.That(createStreamTask.IsCompleted, Is.False);
        await clientConnection.CloseAsync(MultiplexedConnectionCloseError.NoError, default);
        Assert.That(
            async () => await createStreamTask,
            Throws.TypeOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));

        MultiplexedConformanceTestsHelper.CleanupStreams(clientStream1, serverStream1);
    }

    /// <summary>Verify streams cannot be created after closing down the connection.</summary>
    [TestCase(MultiplexedConnectionCloseError.NoError, IceRpcError.ConnectionClosedByPeer)]
    [TestCase(MultiplexedConnectionCloseError.Aborted, IceRpcError.ConnectionAborted)]
    [TestCase(MultiplexedConnectionCloseError.Refused, IceRpcError.ConnectionRefused)]
    [TestCase(MultiplexedConnectionCloseError.ServerBusy, IceRpcError.ServerBusy)]
    [TestCase((MultiplexedConnectionCloseError)255, IceRpcError.ConnectionAborted)]
    public async Task Cannot_create_streams_with_a_closed_connection(
        MultiplexedConnectionCloseError closeError,
        IceRpcError expectedIceRpcError)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        // Act
        await serverConnection.CloseAsync(closeError, CancellationToken.None);

        // Assert
        IceRpcException? exception;

        exception = Assert.ThrowsAsync<IceRpcException>(
            () => clientConnection.AcceptStreamAsync(CancellationToken.None).AsTask());
        Assert.That(exception!.IceRpcError, Is.EqualTo(expectedIceRpcError));

        exception = Assert.ThrowsAsync<IceRpcException>(
            () => clientConnection.CreateStreamAsync(true, default).AsTask());
        Assert.That(exception!.IceRpcError, Is.EqualTo(expectedIceRpcError));
    }

    /// <summary>Verify streams cannot be created after disposing the connection.</summary>
    /// <param name="disposeServerConnection">Whether to dispose the server connection or the client connection.
    /// </param>
    [Test]
    public async Task Cannot_create_streams_with_a_disposed_connection(
        [Values(true, false)] bool disposeServerConnection)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        IMultiplexedConnection disposedConnection = disposeServerConnection ? serverConnection : clientConnection;
        IMultiplexedConnection peerConnection = disposeServerConnection ? clientConnection : serverConnection;
        IMultiplexedStream peerStream = await peerConnection.CreateStreamAsync(true, default).ConfigureAwait(false);
        await peerStream.Output.WriteAsync(_oneBytePayload); // Make sure the stream is started before DisposeAsync

        // Act
        await disposedConnection.DisposeAsync();

        // Assert

        IceRpcException? exception;

        Assert.ThrowsAsync<ObjectDisposedException>(() => disposedConnection.CreateStreamAsync(true, default).AsTask());

        exception = Assert.ThrowsAsync<IceRpcException>(async () =>
            {
                // It can take few writes for the peer to detect the connection closure.
                while (true)
                {
                    FlushResult result = await peerStream.Output.WriteAsync(_oneBytePayload);
                    Assert.That(result.IsCompleted, Is.False);
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                }
            });

        Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.ConnectionAborted));
    }

    [Test]
    public async Task Close_connection()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        IMultiplexedConnection clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        IListener<IMultiplexedConnection> listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();

        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        // Act/Assert

        Assert.That(async () => await clientConnection.CloseAsync(
            MultiplexedConnectionCloseError.NoError,
            CancellationToken.None), Throws.Nothing);

        Assert.That(async () => await serverConnection.CloseAsync(
            MultiplexedConnectionCloseError.NoError,
            CancellationToken.None), Throws.Nothing);
    }

    [Test]
    public async Task Close_connection_simultaneously_on_both_sides()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        IMultiplexedConnection clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        IListener<IMultiplexedConnection> listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        // Act
        Task clientCloseTask = clientConnection.CloseAsync(
            MultiplexedConnectionCloseError.NoError,
            CancellationToken.None);

        Task serverCloseTask = serverConnection.CloseAsync(
            MultiplexedConnectionCloseError.NoError,
            CancellationToken.None);

        // Assert
        Assert.That(() => clientCloseTask, Throws.Nothing);
        Assert.That(() => serverCloseTask, Throws.Nothing);
    }

    /// <summary>Verifies that ConnectAsync can be canceled.</summary>
    [Test]
    public async Task Connect_cancellation()
    {
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);

        using var cts = new CancellationTokenSource();
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var connectTask = clientConnection.ConnectAsync(cts.Token);

        // Act
        cts.Cancel();

        // Assert
        Assert.That(async () => await connectTask, Throws.InstanceOf<OperationCanceledException>());
    }

    /// <summary>Verifies that disabling the idle timeout doesn't abort the connection if it's idle.</summary>
    [Test]
    public async Task Connection_with_no_idle_timeout_is_not_aborted_when_idle()
    {
        // Arrange
        IServiceCollection services = CreateServiceCollection();

        services.AddOptions<SlicTransportOptions>("server").Configure(
            options => options.IdleTimeout = Timeout.InfiniteTimeSpan);
        services.AddOptions<SlicTransportOptions>("client").Configure(
            options => options.IdleTimeout = Timeout.InfiniteTimeSpan);

        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();

        await using var clientConnection = clientTransport.CreateConnection(
            listener.ServerAddress,
            provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
            provider.GetService<SslClientAuthenticationOptions>());

        var connectTask = clientConnection.ConnectAsync(default);
        await using var serverConnection = (await listener.AcceptAsync(default)).Connection;

        _ = await serverConnection.ConnectAsync(default);
        _ = await connectTask;

        ValueTask<IMultiplexedStream> acceptTask = serverConnection.AcceptStreamAsync(default);

        // Act
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Assert
        Assert.That(acceptTask.IsCompleted, Is.False);
        await clientConnection.CloseAsync(MultiplexedConnectionCloseError.NoError, default);
        Assert.That(async () => await acceptTask, Throws.InstanceOf<IceRpcException>());
    }

    /// <summary>Verifies that setting the idle timeout doesn't abort the connection if it's idle.</summary>
    [Test]
    public async Task Connection_with_idle_timeout_is_not_aborted_when_idle(
        [Values(true, false)] bool serverIdleTimeout)
    {
        // Arrange
        IServiceCollection services = CreateServiceCollection();

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

        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
        await using var clientConnection = clientTransport.CreateConnection(
            listener.ServerAddress,
            provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
            provider.GetService<SslClientAuthenticationOptions>());

        var connectTask = clientConnection.ConnectAsync(default);
        await using var serverConnection = (await listener.AcceptAsync(default)).Connection;

        _ = await serverConnection.ConnectAsync(default);
        _ = await connectTask;

        ValueTask<IMultiplexedStream> acceptTask = serverConnection.AcceptStreamAsync(default);

        // Act
        await Task.Delay(TimeSpan.FromSeconds(2));

        // Assert
        Assert.That(acceptTask.IsCompleted, Is.False);
        await clientConnection.CloseAsync(MultiplexedConnectionCloseError.NoError, default);
        Assert.That(async () => await acceptTask, Throws.InstanceOf<IceRpcException>());
    }

    [Test]
    public async Task Create_client_connection_with_unknown_server_address_parameter_fails_with_format_exception()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();

        var serverAddress = new ServerAddress(new Uri("icerpc://foo?unknown-parameter=foo"));

        // Act/Asserts
        Assert.Throws<ArgumentException>(() => clientTransport.CreateConnection(
            serverAddress,
            new MultiplexedConnectionOptions(),
            provider.GetService<SslClientAuthenticationOptions>()));
    }

    [Test]
    public async Task Create_server_connection_with_unknown_server_address_parameter_fails_with_format_exception()
    {
        await using ServiceProvider provider = CreateServiceCollection().BuildServiceProvider(validateScopes: true);
        var serverTransport = provider.GetRequiredService<IMultiplexedServerTransport>();

        var serverAddress = new ServerAddress(new Uri("icerpc://foo?unknown-parameter=foo"));

        // Act/Asserts
        Assert.Throws<ArgumentException>(() => serverTransport.Listen(
            serverAddress,
            new MultiplexedConnectionOptions(),
            provider.GetService<SslServerAuthenticationOptions>()));
    }

    [Test]
    public async Task Connection_operations_fail_with_connection_aborted_error_after_close()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);
        using LocalAndRemoteStreams sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientConnection,
            serverConnection);

        // Act
        await clientConnection.CloseAsync(MultiplexedConnectionCloseError.NoError, default);

        // Assert
        // TODO: OperationAborted or ConnectionAborted? Or another code? See #2382.
        Assert.That(
            async () => await clientConnection.CreateStreamAsync(true, default),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
        Assert.That(
            async () => await clientConnection.AcceptStreamAsync(default),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
        Assert.That(
            async () => await sut.LocalStream.Input.ReadAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
        Assert.That(
            async () => await sut.LocalStream.Output.WriteAsync(_oneBytePayload),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
    }

    [Test]
    public async Task Connection_operations_fail_with_connection_aborted_error_after_dispose()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);
        using LocalAndRemoteStreams sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientConnection,
            serverConnection);

        // Act
        await clientConnection.DisposeAsync();

        // Assert
        Assert.That(
            async () => await clientConnection.CreateStreamAsync(true, default),
            Throws.InstanceOf<ObjectDisposedException>());
        Assert.That(
            async () => await clientConnection.AcceptStreamAsync(default),
            Throws.InstanceOf<ObjectDisposedException>());

        // TODO: OperationAborted or ConnectionAborted? Or another code? See #2382.
        Assert.That(
            async () => await sut.LocalStream.Input.ReadAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
        Assert.That(
            async () => await sut.LocalStream.Output.WriteAsync(_oneBytePayload),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
    }

    [Test]
    public async Task Connection_dispose_aborts_pending_operations_with_operation_aborted_error()
    {
        // Arrange
        IServiceCollection serviceCollection = CreateServiceCollection().AddMultiplexedTransportTest();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxBidirectionalStreams = 1);
        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        using LocalAndRemoteStreams sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientConnection,
            serverConnection);

        ValueTask<IMultiplexedStream> acceptStreamTask = clientConnection.AcceptStreamAsync(default);
        ValueTask<IMultiplexedStream> createStreamTask = clientConnection.CreateStreamAsync(true, default);
        ValueTask<ReadResult> readTask = sut.LocalStream.Input.ReadAsync();

        // Act
        await clientConnection.DisposeAsync();

        // Assert

        Assert.That(
            async () => await acceptStreamTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
        Assert.That(
            async () => await createStreamTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
        Assert.That(
            async () => await readTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
    }

    [Test]
    public async Task Disposing_the_client_connection_aborts_the_server_connection()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        // Act
        await clientConnection.DisposeAsync();

        // Assert
        Assert.That(
            async () => _ = await serverConnection.AcceptStreamAsync(default),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));
    }

    [Test]
    public async Task Disposing_the_connection_closes_the_streams()
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        using LocalAndRemoteStreams sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientConnection,
            serverConnection);

        // Act
        await serverConnection.DisposeAsync();

        // Assert
        Assert.That(async () => await sut.RemoteStream.WritesClosed, Throws.Nothing);
        Assert.That(async () => await sut.RemoteStream.ReadsClosed, Throws.Nothing);
        Assert.That(async () => await sut.LocalStream.WritesClosed, Throws.Nothing);
        Assert.That(async () => await sut.LocalStream.ReadsClosed, Throws.Nothing);
    }

    /// <summary>Write data until the transport flow control start blocking, at this point we start a read task and
    /// ensure that this unblocks the pending write calls.</summary>
    [Test]
    public async Task Flow_control()
    {
        // Arrange
        var payload = new byte[1024 * 64];
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        using LocalAndRemoteStreams sut = await MultiplexedConformanceTestsHelper.CreateAndAcceptStreamAsync(
            clientConnection,
            serverConnection);
        sut.LocalStream.Input.Complete();
        sut.RemoteStream.Output.Complete();

        Task<FlushResult> writeTask;
        while (true)
        {
            writeTask = sut.LocalStream.Output.WriteAsync(payload).AsTask();
            await Task.Delay(TimeSpan.FromMilliseconds(20));
            if (writeTask.IsCompleted)
            {
                await writeTask;
            }
            else
            {
                break;
            }
        }

        // Act
        Task readTask = ReadAsync(sut.RemoteStream);

        // Assert
        Assert.That(async () => await writeTask, Throws.Nothing);
        sut.LocalStream.Output.Complete();
        Assert.That(async () => await readTask, Throws.Nothing);

        static async Task ReadAsync(IMultiplexedStream stream)
        {
            ReadResult readResult = default;
            while (!readResult.IsCompleted)
            {
                readResult = await stream.Input.ReadAsync();
                stream.Input.AdvanceTo(readResult.Buffer.End);
            }
            stream.Input.Complete();
        }
    }

    /// <summary>Verifies that connection cannot exceed the bidirectional stream max count.</summary>
    [Test]
    public async Task Max_bidirectional_stream_stress_test()
    {
        // Arrange
        const int streamMaxCount = 16;
        const int createStreamCount = 32;

        var serviceCollection = CreateServiceCollection().AddMultiplexedTransportTest();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxBidirectionalStreams = streamMaxCount);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        const int payloadSize = 16 * 1024;
        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);

        int streamCount = 0;
        int streamCountMax = 0;
        var mutex = new object();

        var streams = new List<IMultiplexedStream>();
        var tasks = new List<Task>();

        for (int i = 0; i < createStreamCount; ++i)
        {
            tasks.Add(ClientReadWriteAsync());
        }

        // Act
        for (int i = 0; i < createStreamCount; ++i)
        {
            tasks.Add(ServerReadWriteAsync(await serverConnection.AcceptStreamAsync(default)));
        }

        // Assert
        await Task.WhenAll(tasks);
        Assert.That(streamCountMax, Is.LessThanOrEqualTo(streamMaxCount));

        MultiplexedConformanceTestsHelper.CleanupStreams(streams.ToArray());

        async Task ClientReadWriteAsync()
        {
            IMultiplexedStream stream = await clientConnection.CreateStreamAsync(true, default);
            await stream.Output.WriteAsync(payload);
            lock (mutex)
            {
                streams.Add(stream);
                streamCount++;
                streamCountMax = Math.Max(streamCount, streamCountMax);
            }
            stream.Output.Complete();

            while (true)
            {
                ReadResult readResult = await stream.Input.ReadAsync();
                stream.Input.AdvanceTo(readResult.Buffer.End);
                if (readResult.IsCompleted)
                {
                    break;
                }
            }
            stream.Input.Complete();
        }

        async Task ServerReadWriteAsync(IMultiplexedStream stream)
        {
            while (true)
            {
                ReadResult readResult = await stream.Input.ReadAsync();
                stream.Input.AdvanceTo(readResult.Buffer.End);
                if (readResult.IsCompleted)
                {
                    break;
                }
            }
            stream.Input.Complete();

            lock (mutex)
            {
                streamCount--;
            }

            await stream.Output.WriteAsync(payload);
            stream.Output.Complete();
        }
    }

    /// <summary>Verifies that connection cannot exceed the unidirectional stream max count.</summary>
    [Test]
    public async Task Max_unidirectional_stream_stress_test()
    {
        // Arrange
        const int streamMaxCount = 16;
        const int createStreamCount = 32;

        var serviceCollection = CreateServiceCollection().AddMultiplexedTransportTest();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxUnidirectionalStreams = streamMaxCount);

        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);
        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        const int payloadSize = 16 * 1024;
        byte[] payloadData = Enumerable.Range(0, payloadSize).Select(i => (byte)(i % 256)).ToArray();
        var payload = new ReadOnlyMemory<byte>(payloadData);

        int streamCount = 0;
        int streamCountMax = 0;
        var mutex = new object();

        var streams = new List<IMultiplexedStream>();
        var tasks = new List<Task>();
        for (int i = 0; i < createStreamCount; ++i)
        {
            tasks.Add(ClientWriteAsync());
        }

        // Act
        for (int i = 0; i < createStreamCount; ++i)
        {
            tasks.Add(ServerReadAsync(await serverConnection.AcceptStreamAsync(default)));
        }

        // Assert
        await Task.WhenAll(tasks);
        Assert.That(streamCountMax, Is.LessThanOrEqualTo(streamMaxCount));

        MultiplexedConformanceTestsHelper.CleanupStreams(streams.ToArray());

        async Task ClientWriteAsync()
        {
            IMultiplexedStream stream = await clientConnection.CreateStreamAsync(false, default);
            await stream.Output.WriteAsync(payload);
            lock (mutex)
            {
                streams.Add(stream);
                streamCount++;
                streamCountMax = Math.Max(streamCount, streamCountMax);
            }

            // It's important to write enough data to ensure that the last stream frame is not received before the
            // receiver starts reading.
            await stream.Output.WriteAsync(payload);
            await stream.Output.WriteAsync(payload);
            await stream.Output.WriteAsync(payload);
            await stream.Output.WriteAsync(payload);

            stream.Output.Complete();
        }

        async Task ServerReadAsync(IMultiplexedStream stream)
        {
            // The stream is terminated as soon as the last frame of the request is received, so we have
            // to decrement the count here before the request receive completes.
            lock (mutex)
            {
                streamCount--;
            }

            ReadResult readResult;
            do
            {
                readResult = await stream.Input.ReadAsync();
                stream.Input.AdvanceTo(readResult.Buffer.End);
            }
            while (!readResult.IsCompleted);

            stream.Input.Complete();
        }
    }

    [TestCase(MultiplexedConnectionCloseError.NoError, IceRpcError.ConnectionClosedByPeer)]
    [TestCase(MultiplexedConnectionCloseError.Aborted, IceRpcError.ConnectionAborted)]
    [TestCase(MultiplexedConnectionCloseError.Refused, IceRpcError.ConnectionRefused)]
    [TestCase(MultiplexedConnectionCloseError.ServerBusy, IceRpcError.ServerBusy)]
    [TestCase((MultiplexedConnectionCloseError)255, IceRpcError.ConnectionAborted)]
    public async Task Pending_accept_stream_fails_with_peer_close_error_code_on_connection_close(
        MultiplexedConnectionCloseError closeError,
        IceRpcError expectedIceRpcError)
    {
        // Arrange
        await using ServiceProvider provider = CreateServiceCollection()
            .AddMultiplexedTransportTest()
            .BuildServiceProvider(validateScopes: true);

        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        ValueTask<IMultiplexedStream> acceptStreamTask = clientConnection.AcceptStreamAsync(CancellationToken.None);

        // Act
        await serverConnection.CloseAsync(closeError, CancellationToken.None);

        // Assert
        Assert.That(
            async () => await acceptStreamTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(expectedIceRpcError));
    }

    /// <summary>Verify streams cannot be created after closing down the connection.</summary>
    [TestCase(MultiplexedConnectionCloseError.NoError, IceRpcError.ConnectionClosedByPeer)]
    [TestCase(MultiplexedConnectionCloseError.Aborted, IceRpcError.ConnectionAborted)]
    [TestCase(MultiplexedConnectionCloseError.Refused, IceRpcError.ConnectionRefused)]
    [TestCase(MultiplexedConnectionCloseError.ServerBusy, IceRpcError.ServerBusy)]
    [TestCase((MultiplexedConnectionCloseError)255, IceRpcError.ConnectionAborted)]
    public async Task Pending_create_stream_fails_with_peer_close_error_code_on_connection_close(
        MultiplexedConnectionCloseError closeError,
        IceRpcError expectedIceRpcError)
    {
        // Arrange
        IServiceCollection serviceCollection = CreateServiceCollection().AddMultiplexedTransportTest();
        serviceCollection.AddOptions<MultiplexedConnectionOptions>().Configure(
                options => options.MaxBidirectionalStreams = 1);
        await using ServiceProvider provider = serviceCollection.BuildServiceProvider(validateScopes: true);

        var clientConnection = provider.GetRequiredService<IMultiplexedConnection>();
        var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
        await using IMultiplexedConnection serverConnection =
            await MultiplexedConformanceTestsHelper.ConnectAndAcceptConnectionAsync(listener, clientConnection);

        IMultiplexedStream stream1 = await clientConnection.CreateStreamAsync(true, default);
        await stream1.Output.WriteAsync(_oneBytePayload, default); // Ensures the stream is started.

        ValueTask<IMultiplexedStream> stream2CreateStreamTask = clientConnection.CreateStreamAsync(true, default);
        await Task.Delay(100);
        Assert.That(stream2CreateStreamTask.IsCompleted, Is.False);

        // Act
        await serverConnection.CloseAsync(closeError, CancellationToken.None);

        // Assert
        Assert.That(
            async () => await stream2CreateStreamTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(expectedIceRpcError));

        MultiplexedConformanceTestsHelper.CleanupStreams(stream1);
    }

    /// <summary>Creates the service collection used for multiplexed transport conformance tests.</summary>
    protected abstract IServiceCollection CreateServiceCollection();
}

public static class MultiplexedTransportServiceCollectionExtensions
{
    public static IServiceCollection AddMultiplexedTransportTest(this IServiceCollection services) =>
        services.AddSingleton(provider =>
        {
            var listener = provider.GetRequiredService<IListener<IMultiplexedConnection>>();
            var clientTransport = provider.GetRequiredService<IMultiplexedClientTransport>();
            var connection = clientTransport.CreateConnection(
                listener.ServerAddress,
                provider.GetRequiredService<IOptions<MultiplexedConnectionOptions>>().Value,
                provider.GetService<SslClientAuthenticationOptions>());
            return connection;
        });
}
