// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Tests;

[Parallelizable(ParallelScope.All)]
public sealed class ProtocolConnectionTests
{
    public enum ConnectionType
    {
        Client,
        Server
    }

    private static IEnumerable<TestCaseData> ExceptionIsEncodedAsADispatchExceptionSource
    {
        get
        {
            foreach (Protocol protocol in Protocols)
            {
                // an unexpected OCE
                yield return new(protocol, new OperationCanceledException(), StatusCode.UnhandledException);
                yield return new(protocol, new InvalidOperationException(), StatusCode.UnhandledException);
            }

            yield return new(Protocol.IceRpc, new InvalidDataException("invalid data"), StatusCode.InvalidData);
            yield return new(Protocol.IceRpc, new MyException(), StatusCode.ApplicationError);
            yield return new(Protocol.Ice, new InvalidDataException("invalid data"), StatusCode.UnhandledException);
            yield return new(Protocol.Ice, new MyException(), StatusCode.UnhandledException);
        }
    }

    private static List<Protocol> Protocols => new() { Protocol.IceRpc, Protocol.Ice };

    private static IEnumerable<TestCaseData> Protocols_and_client_or_server
    {
        get
        {
            foreach (Protocol protocol in Protocols)
            {
                yield return new TestCaseData(protocol, false);
                yield return new TestCaseData(protocol, true);
            }
        }
    }

    private static IEnumerable<TestCaseData> Protocols_and_oneway_or_twoway
    {
        get
        {
            foreach (Protocol protocol in Protocols)
            {
                yield return new TestCaseData(protocol, false);
                yield return new TestCaseData(protocol, true);
            }
        }
    }

    private static IEnumerable<TestCaseData> Protocols_and_Protocol_connection_operations
    {
        get
        {
            foreach (Protocol protocol in Protocols)
            {
                yield return new TestCaseData(
                    protocol,
                    (IProtocolConnection connection) => connection.ConnectAsync(default),
                    false).SetName($"ConnectAsync {protocol} {{m}}");

                yield return new TestCaseData(
                    protocol,
                    (IProtocolConnection connection) =>
                        connection.InvokeAsync(new OutgoingRequest(new ServiceAddress(protocol))),
                    true).SetName($"InvokeAsync {protocol} {{m}}");

                yield return new TestCaseData(
                    protocol,
                    (IProtocolConnection connection) => connection.ShutdownAsync(),
                    true).SetName($"ShutdownAsync {protocol} {{m}}");
            }
        }
    }

    /// <summary>Verifies that concurrent dispatches on a given connection are limited to MaxDispatches.
    /// </summary>
    [Test]
    public async Task Connection_dispatches_requests_concurrently_up_to_max_dispatches(
        [Values("ice", "icerpc")] string protocolString,
        [Values(1, 70, 200)] int maxDispatches)
    {
        // Arrange
        var protocol = Protocol.Parse(protocolString);
        using var startSemaphore = new SemaphoreSlim(0);
        using var workSemaphore = new SemaphoreSlim(0);
        int count = 0;
        int maxCount = 0;
        var mutex = new object();

        var dispatcher = new InlineDispatcher(async (request, cancellationToken) =>
        {
            // We want to make sure that no more than maxDispatches are executing this dispatcher. So
            // we are tracking the maximum count here (before work) and decrement this count immediately in the
            // "work". Without the decrement, the count (and max count) could be greater than
            // maxDispatches.
            IncrementCount();
            startSemaphore.Release();
            await workSemaphore.WaitAsync(cancellationToken);
            DecrementCount();
            return new OutgoingResponse(request);

            void DecrementCount()
            {
                lock (mutex)
                {
                    count--;
                }
            }

            void IncrementCount()
            {
                lock (mutex)
                {
                    count++;
                    maxCount = Math.Max(maxCount, count);
                }
            }
        });

        var services = new ServiceCollection().AddProtocolTest(
            protocol,
            serverConnectionOptions: new ConnectionOptions
            {
                Dispatcher = dispatcher,
                MaxDispatches = maxDispatches
            });

        // TODO: this configuration is very confusing. AddProtocolTest does not create a Server but use some
        // ServerOptions and does not forward these ServerOptions to the underlying transport.
        // We add "100" to make sure the limit does not come from MaxBidirectionalStreams.
        services.AddOptions<MultiplexedConnectionOptions>().Configure(
            options => options.MaxBidirectionalStreams = maxDispatches + 100);

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var requestList = new List<OutgoingRequest>();
        var responseTasks = new List<Task<IncomingResponse>>();

        // Act
        for (int i = 0; i < maxDispatches + 1; ++i)
        {
            var request = new OutgoingRequest(new ServiceAddress(protocol));
            requestList.Add(request);
            responseTasks.Add(sut.Client.InvokeAsync(request));
        }
        // wait for maxDispatches dispatches to start
        for (int i = 0; i < maxDispatches; ++i)
        {
            await startSemaphore.WaitAsync();
        }

        // Assert
        for (int i = 0; i < maxDispatches + 1; ++i)
        {
            Assert.That(responseTasks[i].IsCompleted, Is.False);
        }

        workSemaphore.Release(maxDispatches + 1);

        await Task.WhenAll(responseTasks);
        Assert.That(maxCount, Is.EqualTo(maxDispatches));

        // Cleanup
        foreach (OutgoingRequest request in requestList)
        {
            request.Dispose();
        }
    }

    /// <summary>Verifies that when dispatches are blocked waiting for the dispatch semaphore that aborting the server
    /// connection correctly cancels the dispatch semaphore wait. If the dispatch semaphore wait wasn't canceled, the
    /// DisposeAsync call would hang because it waits for the read semaphore to be released.</summary>
    [Test]
    public async Task Connection_with_dispatches_waiting_for_dispatch_unblocks_on_dispose(
        [Values("ice", "icerpc")] string protocolString)
    {
        // Arrange

        var protocol = Protocol.Parse(protocolString);
        using var dispatcher = new TestDispatcher();
        await using var provider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                serverConnectionOptions: new ConnectionOptions
                {
                    Dispatcher = dispatcher,
                    MaxDispatches = 1
                })
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Perform two invocations. The first blocks so the second won't be dispatched. It will block on the protocol
        // connection's internal dispatch semaphore which is canceled on dispose.

        // Wait for the first invocation to be dispatched.
        using var request1 = new OutgoingRequest(new ServiceAddress(protocol));
        Task invokeTask1 = sut.Client.InvokeAsync(request1);
        await dispatcher.DispatchStart;

        // Wait to make sure the second request is received and blocked on the protocol connection's internal dispatch
        // semaphore.
        using var request2 = new OutgoingRequest(new ServiceAddress(protocol));
        Task invokeTask2 = sut.Client.InvokeAsync(request2);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Act / Assert
        // If the protocol connection's internal dispatch semaphore wasn't canceled, the DisposeAsync would hang.
        await sut.Server.DisposeAsync();

        // TODO: temporary

        if (protocol == Protocol.Ice)
        {
            Assert.That(
                async () => await invokeTask1,
                Throws.InstanceOf<IceRpcException>()
                    .With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted).Or
                    .With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
            Assert.That(
                async () => await invokeTask2,
                Throws.InstanceOf<IceRpcException>()
                    .With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted).Or
                    .With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
        }
        else
        {
            Assert.That(
                async () => await invokeTask1,
                Throws.InstanceOf<IceRpcException>()
                    .With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted).Or
                    .With.Property("IceRpcError").EqualTo(IceRpcError.TruncatedData));
            Assert.That(
                async () => await invokeTask2,
                Throws.InstanceOf<IceRpcException>()
                    .With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted).Or
                    .With.Property("IceRpcError").EqualTo(IceRpcError.TruncatedData));
        }
    }

    /// <summary>Verifies that when a exception other than a DispatchException is thrown
    /// during the dispatch, we encode a DispatchException with the expected status code.</summary>
    [Test, TestCaseSource(nameof(ExceptionIsEncodedAsADispatchExceptionSource))]
    public async Task Exception_is_encoded_as_a_dispatch_exception(
        Protocol protocol,
        Exception thrownException,
        StatusCode statusCode)
    {
        var dispatcher = new InlineDispatcher((request, cancellationToken) => throw thrownException);

        await using var provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(protocol));

        // Act
        var response = await sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(response.StatusCode, Is.EqualTo(statusCode));
    }

    /// <summary>Verifies that disposing a connection that was not connected completes the
    /// <see cref="IProtocolConnection.Closed" /> task.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Closed_completes_when_disposing_not_connected_connection(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        // Act
        await sut.Client.DisposeAsync();

        // Assert
        Assert.That(async () => await sut.Client.Closed, Is.Null);
    }

    /// <summary>Verifies that ShutdownRequested completes when idle.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task ShutdownRequested_completes_when_idle(Protocol protocol)
    {
        // Arrange

        // With the ice protocol, the idle timeout is used for both the transport connection and protocol connection
        // idle timeout. We need to set the server side idle timeout to ensure the server-side connection sends a keep
        // alive to prevent the client transport connection to be closed because it's idle.
        ConnectionOptions? serverConnectionOptions = protocol == Protocol.Ice ?
            new ConnectionOptions
            {
                IdleTimeout = TimeSpan.FromMilliseconds(800),
            } :
            null;

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                clientConnectionOptions: new ConnectionOptions { IdleTimeout = TimeSpan.FromMilliseconds(500) },
                serverConnectionOptions: serverConnectionOptions)
            .BuildServiceProvider(validateScopes: true);

        var startTime = TimeSpan.FromMilliseconds(Environment.TickCount64);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        await sut.Client.ShutdownRequested;

        // Assert
        Assert.That(
            TimeSpan.FromMilliseconds(Environment.TickCount64) - startTime,
            Is.GreaterThan(TimeSpan.FromMilliseconds(490)));
    }

    /// <summary>Verifies that ShutdownRequested completes when idle and after the idle time has been deferred by the
    /// reading of the payload.</summary>
    [Test, TestCaseSource(nameof(Protocols_and_oneway_or_twoway))]
    public async Task ShutdownRequested_completes_when_idle_and_idle_timeout_deferred_by_payload_read(
        Protocol protocol,
        bool isOneway)
    {
        // Arrange

        // With the ice protocol, the idle timeout is used for both the transport and protocol idle timeout. We need
        // to set the server side idle timeout to ensure the server-side connection sends keep alive to prevent the
        // client transport connection to be closed because it's idle.
        ConnectionOptions? serverConnectionOptions = protocol == Protocol.Ice ?
            new ConnectionOptions
            {
                IdleTimeout = TimeSpan.FromMilliseconds(800),
            } :
            null;

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                clientConnectionOptions: new ConnectionOptions
                {
                    IdleTimeout = TimeSpan.FromMilliseconds(500),
                    Dispatcher = ServiceNotFoundDispatcher.Instance
                },
                serverConnectionOptions: serverConnectionOptions)
            .BuildServiceProvider(validateScopes: true);

        long startTime = Environment.TickCount64;

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        {
            using var request = new OutgoingRequest(new ServiceAddress(protocol))
            {
                IsOneway = isOneway,
                Payload = new DelayPipeReader(TimeSpan.FromMilliseconds(520))
            };
            _ = await sut.Client.InvokeAsync(request);
        }

        // Act
        await sut.Client.ShutdownRequested;

        // Assert
        Assert.That(
            TimeSpan.FromMilliseconds(Environment.TickCount64 - startTime),
            Is.GreaterThan(TimeSpan.FromMilliseconds(990)).And.LessThan(TimeSpan.FromSeconds(2)));
    }

    /// <summary>Verifies that ShutdownRequested completes when idle and after the idle time has been deferred by the
    /// writing of the payload.</summary>
    [Test, TestCaseSource(nameof(Protocols_and_oneway_or_twoway))]
    public async Task ShutdownRequested_completes_when_idle_and_idle_timeout_deferred_by_payload_write(
        Protocol protocol,
        bool isOneway)
    {
        // Arrange
        // With the ice protocol, the idle timeout is used for both the transport and protocol idle timeout. We need
        // to set the server side idle timeout to ensure the server-side connection sends keep alive to prevent the
        // client transport connection to be closed because it's idle.
        TimeSpan idleTimeout = protocol == Protocol.Ice ? TimeSpan.FromMilliseconds(800) : TimeSpan.FromSeconds(60);
        ConnectionOptions? serverConnectionOptions =
            new ConnectionOptions
            {
                IdleTimeout = idleTimeout,
                Dispatcher = new InlineDispatcher(async (request, cancellationToken) =>
                {
                    ReadResult result;
                    do
                    {
                        result = await request.Payload.ReadAsync(cancellationToken);
                        request.Payload.AdvanceTo(result.Buffer.End);
                    }
                    while (!result.IsCompleted && !result.IsCanceled);
                    return new OutgoingResponse(request);
                })
            };

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                clientConnectionOptions: new ConnectionOptions
                {
                    IdleTimeout = TimeSpan.FromMilliseconds(500),
                },
                serverConnectionOptions: serverConnectionOptions)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var pipe = new Pipe();
        using var request = new OutgoingRequest(new ServiceAddress(protocol))
        {
            Payload = pipe.Reader,
            IsOneway = isOneway
        };
        await pipe.Writer.WriteAsync(new ReadOnlyMemory<byte>(new byte[10]));
        long startTime = Environment.TickCount64;
        var invokeTask = sut.Client.InvokeAsync(request);
        await Task.Delay(TimeSpan.FromMilliseconds(550));
        pipe.Writer.Complete();
        await invokeTask;

        // Act
        await sut.Client.ShutdownRequested;

        // Assert
        Assert.That(
            TimeSpan.FromMilliseconds(Environment.TickCount64 - startTime),
            Is.GreaterThan(TimeSpan.FromMilliseconds(990)).And.LessThan(TimeSpan.FromSeconds(2)));
    }

    /// <summary>Verifies that an abortive shutdown completes Closed.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connection_abort_completes_closed(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        // Initialize the connection.
        await sut.ConnectAsync();

        // Act
        await sut.Client.DisposeAsync();

        // Assert
        Assert.That(
            await sut.Server.Closed,
            Is.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionAborted));
    }

    /// <summary>Verifies that a ConnectAsync failure completes Closed.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task ConnectAsync_failure_completes_closed(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        Task connectTask = sut.Client.ConnectAsync(default);

        // Act
        await sut.DisposeListenerAsync(); // dispose the listener to trigger the ConnectAsync failure.

        // Assert
        Assert.That(async () => await connectTask, Throws.InstanceOf<IceRpcException>());

        Assert.That(
            await sut.Client.Closed,
            Is.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionRefused));
    }

    /// <summary>Verifies that the cancellation token given to dispatch is not cancelled.</summary>
    [Test, TestCaseSource(nameof(Protocols_and_oneway_or_twoway))]
    public async Task Dispatch_cancellation_token_is_not_canceled(Protocol protocol, bool isOneway)
    {
        // Arrange
        var tcs = new TaskCompletionSource<bool>();

        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            tcs.SetResult(cancellationToken.IsCancellationRequested);
            return new(new OutgoingResponse(request));
        });

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        using var request = new OutgoingRequest(new ServiceAddress(protocol)) { IsOneway = isOneway };
        _ = await sut.Client.InvokeAsync(request);
        bool tokenCanceled = await tcs.Task;

        // Assert
        Assert.That(tokenCanceled, Is.False);
    }

    /// <summary>Verifies that an abortive shutdown of a server connection cancels dispatches.</summary>
    // TODO: split this test in ice and icerpc versions since the exception is different.
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Abortive_shutdown_cancels_dispatches(Protocol protocol)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        _ = FulfillShutdownRequestAsync(sut.Client);

        using var request = new OutgoingRequest(new ServiceAddress(protocol));
        Task<IncomingResponse> invokeTask = sut.Client.InvokeAsync(request);

        await dispatcher.DispatchStart; // Wait for the dispatch to start
        Task shutdownTask = sut.Server.ShutdownAsync();

        // Act
        await sut.Server.DisposeAsync();

        // Assert
        Assert.That(() => dispatcher.DispatchComplete, Is.InstanceOf<OperationCanceledException>());

        try
        {
            IncomingResponse response = await invokeTask;

            // expected with ice
            Assert.That(response.StatusCode, Is.EqualTo(StatusCode.UnhandledException));
        }
        catch (IceRpcException exception)
        {
            // expected with icerpc
            Assert.That(
                exception.IceRpcError,
                Is.EqualTo(IceRpcError.ConnectionAborted).Or.EqualTo(IceRpcError.TruncatedData));
        }

        // TODO: temporary if
        if (protocol == Protocol.IceRpc)
        {
            Assert.That(
                async () => await shutdownTask,
                Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
        }
    }

    /// <summary>Verifies that disposing the client connection aborts pending invocations, the invocations will fail
    /// with <see cref="ObjectDisposedException" />.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Dispose_aborts_pending_invocations(Protocol protocol)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(protocol));
        Task invokeTask = sut.Client.InvokeAsync(request);
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        _ = sut.Client.DisposeAsync().AsTask();

        // Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(async () => await invokeTask);
        Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.OperationAborted));
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Dispose_aborts_connect(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        // TODO: temporary
        if (protocol == Protocol.IceRpc)
        {
            Task connectTask = sut.Client.ConnectAsync(default);
            await Task.Delay(TimeSpan.FromMilliseconds(100));

            // Act
            await sut.Client.DisposeAsync();

            // Assert
            Assert.That(
                async () => await connectTask,
                Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
        }
    }

    /// <summary>Ensures that the sending of a request after shutdown fails with <see cref="IceRpcException" />.
    /// </summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Invoke_on_shutdown_connection_fails_with_invocation_refused(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        _ = FulfillShutdownRequestAsync(sut.Server);
        Task shutdownTask = sut.Client.ShutdownAsync();

        // Act/Assert
        IceRpcException? exception = Assert.ThrowsAsync<IceRpcException>(
            () => sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(protocol))));
        Assert.That(exception!.IceRpcError, Is.EqualTo(IceRpcError.InvocationRefused));

        // Cleanup
        await shutdownTask;
    }

    /// <summary>Ensures that the sending a request after dispose fails.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Invoke_on_connection_fails_after_dispose(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        Task disposeTask = sut.Client.DisposeAsync().AsTask();

        // Act/Assert
        Assert.ThrowsAsync<ObjectDisposedException>(() => sut.Client.InvokeAsync(
            new OutgoingRequest(new ServiceAddress(protocol))));
    }

    /// <summary>Ensures that calling ConnectAsync, ShutdownAsync or InvokeAsync raise ObjectDisposedException if the
    /// connection is disposed.</summary>
    [Test, TestCaseSource(nameof(Protocols_and_Protocol_connection_operations))]
    public async Task Operation_throws_object_disposed_exception(
        Protocol protocol,
        Func<IProtocolConnection, Task> operation,
        bool connect)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        if (connect)
        {
            await sut.ConnectAsync();
        }
        await sut.Client.DisposeAsync();

        // Act/Assert
        Assert.That(() => operation(sut.Client), Throws.InstanceOf<ObjectDisposedException>());
    }

    /// <summary>Ensures that the request payload is completed on a valid request.</summary>
    [Test, TestCaseSource(nameof(Protocols_and_oneway_or_twoway))]
    public async Task Payload_completed_on_valid_request(Protocol protocol, bool isOneway)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        using var request = new OutgoingRequest(new ServiceAddress(protocol))
        {
            IsOneway = isOneway,
            Payload = payloadDecorator
        };

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.Null);

        // Cleanup
        await responseTask;
    }

    /// <summary>Ensures that the response payload is completed on a valid response.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Payload_completed_on_valid_response(Protocol protocol)
    {
        // Arrange
        var payloadDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
                new(new OutgoingResponse(request)
                {
                    Payload = payloadDecorator
                }));

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(protocol));

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadDecorator.Completed, Is.Null);

        // Cleanup
        _ = await responseTask;
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Receive_payload(Protocol protocol)
    {
        // Arrange
        byte[] expectedPayload = Enumerable.Range(0, 4096).Select(p => (byte)p).ToArray();
        var dispatcher = new InlineDispatcher(async (request, cancellationToken) =>
        {
            ReadResult readResult = await request.Payload.ReadAtLeastAsync(
                expectedPayload.Length + 1,
                cancellationToken);
            request.Payload.AdvanceTo(readResult.Buffer.End);
            return new OutgoingResponse(request)
            {
                Payload = PipeReader.Create(new ReadOnlySequence<byte>(expectedPayload))
            };
        });
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(protocol))
        {
            Payload = PipeReader.Create(new ReadOnlySequence<byte>(expectedPayload))
        };

        // Act
        IncomingResponse response = await sut.Client.InvokeAsync(request);

        // Assert
        ReadResult readResult = await response.Payload.ReadAtLeastAsync(expectedPayload.Length + 1, default);
        Assert.That(readResult.IsCompleted, Is.True);
        Assert.That(readResult.Buffer.ToArray(), Is.EqualTo(expectedPayload));
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Request_with_large_header(Protocol protocol)
    {
        // Arrange
        // This large value should be large enough to create multiple buffers for the request header.
        var expectedValue = new Dictionary<string, string>
        {
            ["ctx"] = new string('C', 4096)
        };
        byte[]? field = null;
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
        {
            field = request.Fields[RequestFieldKey.Context].ToArray();
            return new(new OutgoingResponse(request));
        });
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        using var request = new OutgoingRequest(new ServiceAddress(protocol))
        {
            Fields = new Dictionary<RequestFieldKey, OutgoingFieldValue>
            {
                [RequestFieldKey.Context] = new OutgoingFieldValue(
                    (ref SliceEncoder encoder) => encoder.EncodeDictionary(
                        expectedValue,
                        (ref SliceEncoder encoder, string key) => encoder.EncodeString(key),
                        (ref SliceEncoder encoder, string value) => encoder.EncodeString(value)))
            }
        };

        // Act
        _ = await sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(field, Is.Not.Null);
        Assert.That(DecodeField(), Is.EqualTo(expectedValue));

        Dictionary<string, string> DecodeField()
        {
            SliceEncoding encoding = protocol == Protocol.Ice ? SliceEncoding.Slice1 : SliceEncoding.Slice2;
            var decoder = new SliceDecoder(field, encoding);
            return decoder.DecodeDictionary(
                count => new Dictionary<string, string>(count),
                (ref SliceDecoder decoder) => decoder.DecodeString(),
                (ref SliceDecoder decoder) => decoder.DecodeString());
        }
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Send_payload(Protocol protocol)
    {
        // Arrange
        byte[] expectedPayload = Enumerable.Range(0, 4096).Select(p => (byte)p).ToArray();
        byte[]? receivedPayload = null;
        var dispatcher = new InlineDispatcher(async (request, cancellationToken) =>
        {
            ReadResult readResult = await request.Payload.ReadAtLeastAsync(
                expectedPayload.Length + 1,
                cancellationToken);
            receivedPayload = readResult.Buffer.ToArray();
            request.Payload.AdvanceTo(readResult.Buffer.End);
            return new OutgoingResponse(request);
        });
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        using var request = new OutgoingRequest(new ServiceAddress(protocol))
        {
            Payload = PipeReader.Create(new ReadOnlySequence<byte>(expectedPayload))
        };

        // Act
        await sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(receivedPayload, Is.EqualTo(expectedPayload));
    }

    /// <summary>Verifies that ConnectAsync is canceled by its cancellation token.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connect_cancellation(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act/Assert
        Assert.That(
            async () => await sut.Client.ConnectAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Shutdown_throws_if_not_connected(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        // Act/Assert
        Assert.That(async () => await sut.Client.ShutdownAsync(), Throws.InstanceOf<InvalidOperationException>());
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connect_throws_object_disposed_exception_after_dispose(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        await sut.Client.DisposeAsync();

        // Act/Assert
        Assert.That(async () => await sut.Client.ConnectAsync(default), Throws.TypeOf<ObjectDisposedException>());
    }

    /// <summary>Verifies connection shutdown is successful</summary>
    [Test, TestCaseSource(nameof(Protocols_and_client_or_server))]
    public async Task Shutdown_connection(Protocol protocol, bool closeClientSide)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        _ = FulfillShutdownRequestAsync(closeClientSide ? sut.Server : sut.Client);

        // Act
        Task shutdownTask = (closeClientSide ? sut.Client : sut.Server).ShutdownAsync();

        // Assert
        Assert.That(async () => await shutdownTask, Throws.Nothing);
    }

    /// <summary>Ensure that ShutdownAsync fails with an IceRpcException if ConnectAsync fails.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Shutdown_fails_if_connect_fails(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        Task connectTask = sut.Client.ConnectAsync(default);
        await Task.Delay(TimeSpan.FromMilliseconds(50)); // give ConnectAsync the time to start
        await sut.DisposeListenerAsync(); // dispose the listener to trigger the connection establishment failure.

        // Act/Assert
        Assert.That(
            async () => await connectTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionRefused));

        Assert.That(
            async () => await sut.Client.ShutdownAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.ConnectionRefused));
    }

    /// <summary>Ensure that ShutdownAsync waits when ConnectAsync is in progress.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Shutdown_waits_when_connect_is_in_progress(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        using var cts = new CancellationTokenSource();
        Task connectTask = sut.Client.ConnectAsync(cts.Token);
        Task shutdownTask = sut.Client.ShutdownAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(100)); // make sure shutdown is running

        // Assert
        Assert.That(shutdownTask.IsCompleted, Is.False);
        cts.Cancel();
        Assert.That(async () => await connectTask, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(
            async () => await shutdownTask,
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
    }

    /// <summary>Ensure that ShutdownAsync fails if ConnectAsync is canceled.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Shutdown_fails_on_connect_cancellation(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act/Assert
        Assert.That(
            async () => await sut.Client.ConnectAsync(cts.Token),
            Throws.InstanceOf<OperationCanceledException>());
        Assert.That(
            async () => await sut.Client.ShutdownAsync(),
            Throws.InstanceOf<IceRpcException>().With.Property("IceRpcError").EqualTo(IceRpcError.OperationAborted));
    }

    /// <summary>Verifies that the cancellation of a shutdown does not abort invocations.</summary>
    [Test, TestCaseSource(nameof(Protocols_and_client_or_server))]
    public async Task Shutdown_cancellation_does_not_abort_invocations(Protocol protocol, bool closeClientSide)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        IServiceCollection services = new ServiceCollection().AddProtocolTest(protocol, dispatcher);
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(protocol));
        Task invokeTask = sut.Client.InvokeAsync(request);
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));

        // Act
        Task shutdownTask = (closeClientSide ? sut.Client : sut.Server).ShutdownAsync(cancellationToken: cts.Token);

        // Assert
        Assert.That(async () => await shutdownTask, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(invokeTask.IsCompleted, Is.False);

        // Cleanup
        dispatcher.ReleaseDispatch();
        Assert.That(async () => await invokeTask, Throws.Nothing);
    }

    /// <summary>Verifies that the connection shutdown waits for pending invocations and dispatches to finish.</summary>
    [Test, TestCaseSource(nameof(Protocols_and_client_or_server))]
    public async Task Shutdown_waits_for_pending_invocation_and_dispatch_to_finish(
        Protocol protocol,
        bool closeClientSide)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        _ = FulfillShutdownRequestAsync(closeClientSide ? sut.Server : sut.Client);

        using var request = new OutgoingRequest(new ServiceAddress(protocol));
        Task<IncomingResponse> invokeTask = sut.Client.InvokeAsync(request);
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        Task shutdownTask = (closeClientSide ? sut.Client : sut.Server).ShutdownAsync();

        // Assert
        Assert.That(invokeTask.IsCompleted, Is.False);
        Assert.That(shutdownTask.IsCompleted, Is.False);
        dispatcher.ReleaseDispatch();

        Assert.That(async () => await invokeTask, Throws.Nothing);

        // Complete the response, shutdown could hang otherwise if the response stream reading side is not closed.
        (await invokeTask).Payload.Complete();

        Assert.That(async () => await shutdownTask, Throws.Nothing);
    }

    private static async Task FulfillShutdownRequestAsync(IProtocolConnection connection)
    {
        await connection.ShutdownRequested;
        try
        {
            await connection.ShutdownAsync();
        }
        catch
        {
            // ignore all exceptions
        }
    }

    private sealed class DelayPipeReader : PipeReader
    {
        private readonly TimeSpan _delay;

        public override void AdvanceTo(SequencePosition consumed)
        {
        }

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
        }

        public override void CancelPendingRead()
        {
        }

        public override void Complete(Exception? exception = null)
        {
        }

        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(_delay, cancellationToken);
            return new ReadResult(new ReadOnlySequence<byte>(new byte[10]), isCanceled: false, isCompleted: true);
        }

        public override bool TryRead(out ReadResult result)
        {
            result = new ReadResult();
            return false;
        }

        internal DelayPipeReader(TimeSpan delay) => _delay = delay;
    }
}
