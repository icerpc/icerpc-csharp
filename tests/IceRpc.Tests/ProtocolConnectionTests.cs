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

                yield return new(protocol, new InvalidDataException("invalid data"), StatusCode.InvalidData);
                yield return new(protocol, new MyException(), StatusCode.UnhandledException);
                yield return new(protocol, new InvalidOperationException(), StatusCode.UnhandledException);
            }
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

        // Perform two invocations. The first blocks so the second won't be dispatched.
        // It will block on the protocol connection's internal dispatch semaphore which is canceled on dispose.

        // Wait for the first invocation to be dispatched.
        using var request1 = new OutgoingRequest(new ServiceAddress(protocol));
        _ = sut.Client.InvokeAsync(request1);
        await dispatcher.DispatchStart;

        // Wait to make sure the second request is received and blocked on the
        // protocol connection's internal dispatch semaphore.
        using var request2 = new OutgoingRequest(new ServiceAddress(protocol));
        _ = sut.Client.InvokeAsync(request2);
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Act / Assert
        // If the protocol connection's internal dispatch semaphore wasn't canceled, the DisposeAsync will hang.
        await sut.Server.DisposeAsync();
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
        Assert.That(
            async () => (await response.DecodeDispatchExceptionAsync(request)).StatusCode,
            Is.EqualTo(statusCode));
        Assert.That(response.Fields.ContainsKey(ResponseFieldKey.RetryPolicy), Is.False);
    }

    /// <summary>Verifies that disposing a connection that was not connected completes the
    /// <see cref="ProtocolConnection.ShutdownComplete" /> task.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task ShutdownComplete_completes_when_disposing_not_connected_connection(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        // Act
        await sut.Client.DisposeAsync();

        // Assert
        Assert.That(async () => await sut.Client.ShutdownComplete, Throws.Nothing);
    }

    /// <summary>Verifies that ShutdownComplete completes when idle.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task ShutdownComplete_completes_when_idle(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                serverConnectionOptions: new ConnectionOptions { IdleTimeout = TimeSpan.FromMilliseconds(500) })
            .BuildServiceProvider(validateScopes: true);

        TimeSpan? clientIdleCalledTime = null;
        TimeSpan? serverIdleCalledTime = null;

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        await Task.WhenAll(WaitForClientConnectionAsync(), WaitForServerConnectionAsync());

        // Assert
        Assert.That(clientIdleCalledTime, Is.Not.Null);
        Assert.That(serverIdleCalledTime, Is.Not.Null);
        Assert.That(clientIdleCalledTime!.Value, Is.GreaterThan(TimeSpan.FromMilliseconds(490)));
        Assert.That(serverIdleCalledTime!.Value, Is.GreaterThan(TimeSpan.FromMilliseconds(490)));

        async Task WaitForClientConnectionAsync()
        {
            await sut.Client.ShutdownComplete;
            clientIdleCalledTime ??= TimeSpan.FromMilliseconds(Environment.TickCount64);
        }

        async Task WaitForServerConnectionAsync()
        {
            await sut.Server.ShutdownComplete;
            serverIdleCalledTime ??= TimeSpan.FromMilliseconds(Environment.TickCount64);
        }
    }

    /// <summary>Verifies that ShutdownComplete completes when idle and after the idle time has been deferred.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task ShutdownComplete_completes_when_idle_and_idle_timeout_deferred(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                serverConnectionOptions: new ConnectionOptions
                {
                    IdleTimeout = TimeSpan.FromMilliseconds(500),
                    Dispatcher = ServiceNotFoundDispatcher.Instance
                })
            .BuildServiceProvider(validateScopes: true);

        long startTime = Environment.TickCount64;
        long? clientIdleCalledTime = null;
        long? serverIdleCalledTime = null;

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        Task clientTask = WaitForClientConnectionAsync();
        Task serverTask = WaitForServerConnectionAsync();

        {
            using var request = new OutgoingRequest(new ServiceAddress(protocol));
            _ = await sut.Client.InvokeAsync(request);
        }

        // Act
        await Task.WhenAll(clientTask, serverTask);

        // Assert
        Assert.That(
            TimeSpan.FromMilliseconds(clientIdleCalledTime!.Value),
            Is.GreaterThan(TimeSpan.FromMilliseconds(490)).And.LessThan(TimeSpan.FromSeconds(2)));
        Assert.That(
            TimeSpan.FromMilliseconds(serverIdleCalledTime!.Value),
            Is.GreaterThan(TimeSpan.FromMilliseconds(490)).And.LessThan(TimeSpan.FromSeconds(2)));

        async Task WaitForClientConnectionAsync()
        {
            try
            {
                await sut.Client.ShutdownComplete;
            }
            catch when (protocol == Protocol.Ice)
            {
                // TODO: with ice, the shutdown of the peer currently triggers an abort
            }
            clientIdleCalledTime ??= Environment.TickCount64 - startTime;
        }

        async Task WaitForServerConnectionAsync()
        {
            try
            {
                await sut.Server.ShutdownComplete;
            }
            catch when (protocol == Protocol.Ice)
            {
                // TODO: with ice, the shutdown of the peer currently triggers an abort
            }
            serverIdleCalledTime ??= Environment.TickCount64 - startTime;
        }
    }

    /// <summary>Verifies that an abortive shutdown completes ShutdownComplete.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connection_abort_completes_shutdown_complete(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        // Initialize the connection.
        await sut.ConnectAsync();

        try
        {
            await sut.Client.ShutdownAsync(new CancellationToken(true));
        }
        catch (OperationCanceledException)
        {
        }

        // Act
        await sut.Client.DisposeAsync();

        // Assert
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(
            async () => await sut.Server.ShutdownComplete);
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.ClosedByAbort));
    }

    /// <summary>Verifies that a ConnectAsync failure completes ShutdownComplete.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task ConnectAsync_failure_completes_shutdown_complete(Protocol protocol)
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
        Assert.That(async () => await connectTask, Throws.InstanceOf<ConnectionException>());
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(
            async () => await sut.Client.ShutdownComplete);
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.ClosedByAbort));
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

    /// <summary>Verifies that disposing the server connection cancels dispatches.</summary>
    // TODO: split this test in ice and icerpc versions since the exception is different.
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Dispose_cancels_dispatches(Protocol protocol)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        using var request = new OutgoingRequest(new ServiceAddress(protocol));
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        await sut.Server.DisposeAsync();

        // Assert
        Assert.That(async () => await dispatcher.DispatchComplete, Throws.InstanceOf<OperationCanceledException>());

        try
        {
            IncomingResponse response = await responseTask;
            Assert.That(response.StatusCode, Is.EqualTo(StatusCode.UnhandledException));
        }
        catch (TruncatedDataException)
        {
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
        await sut.Client.DisposeAsync();

        // Assert
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(async () => await invokeTask);
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.OperationAborted));
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Dispose_waits_for_connect_completion(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        Task connectTask = sut.Client.ConnectAsync(default);
        ValueTask disposeTask = sut.Client.DisposeAsync();
        await Task.Delay(TimeSpan.FromSeconds(1));

        // Act/Assert
        Assert.That(disposeTask.IsCompleted, Is.False);
        Assert.That(connectTask.IsCompleted, Is.False);

        await sut.AcceptAsync();

        Assert.That(async () => await connectTask, Throws.Nothing);
        Assert.That(async () => await disposeTask, Throws.Nothing);
    }

    /// <summary>Ensures that the sending of a request after shutdown fails with <see
    /// cref="ConnectionException" />.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Invoke_on_shutdown_connection_fails_with_connection_closed(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        Task shutdownTask = sut.Client.ShutdownAsync();

        // Act/Assert
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(
            () => sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(protocol))));
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.ClosedByShutdown));

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

        await disposeTask;
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

    /// <summary>Ensures that calling ConnectAsync, ShutdownAsync or InvokeAsync throws
    /// ConnectionException with ClosedByShutdown if the connection is closed.</summary>
    [Test, TestCaseSource(nameof(Protocols_and_Protocol_connection_operations))]
    public async Task Operation_throws_connection_exception_with_closed_error_code(
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
        await sut.Client.ShutdownAsync();

        // Act/Assert
        ConnectionException? exception = Assert.Throws<ConnectionException>(() => operation(sut.Client));
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.ClosedByShutdown));
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

    /// <summary>Ensures that the request payload writer is completed on valid request.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task PayloadWriter_completed_with_valid_request(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        using var request = new OutgoingRequest(new ServiceAddress(protocol));
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        request.Use(writer =>
            {
                var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                payloadWriterSource.SetResult(payloadWriterDecorator);
                return payloadWriterDecorator;
            });

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.Null);

        // Cleanup
        await responseTask;
    }

    /// <summary>Ensures that the request payload writer is completed on valid response.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task PayloadWriter_completed_with_valid_response(Protocol protocol)
    {
        // Arrange
        var payloadWriterSource = new TaskCompletionSource<PayloadPipeWriterDecorator>();
        var dispatcher = new InlineDispatcher((request, cancellationToken) =>
            {
                var response = new OutgoingResponse(request);
                response.Use(writer =>
                    {
                        var payloadWriterDecorator = new PayloadPipeWriterDecorator(writer);
                        payloadWriterSource.SetResult(payloadWriterDecorator);
                        return payloadWriterDecorator;
                    });
                return new(response);
            });

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();

        using var request = new OutgoingRequest(new ServiceAddress(protocol));

        // Act
        Task<IncomingResponse> responseTask = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await (await payloadWriterSource.Task).Completed, Is.Null);

        // Cleanup
        await responseTask;
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

    /// <summary>Verifies that connect establishment timeouts after the <see cref="ConnectionOptions.ConnectTimeout" />
    /// time period.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connect_timeout(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                clientConnectionOptions: new ConnectionOptions { ConnectTimeout = TimeSpan.FromSeconds(1) })
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        // Act/Assert
        Assert.That(async () => await sut.Client.ConnectAsync(default), Throws.TypeOf<TimeoutException>());
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connect_throws_connection_exception_after_shutdown(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                clientConnectionOptions: new ConnectionOptions { ConnectTimeout = TimeSpan.FromSeconds(1) })
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        await sut.Client.ShutdownAsync();

        // Act/Assert
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(
            () => sut.Client.ConnectAsync(default));
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.ClosedByShutdown));
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connect_throws_object_disposed_exception_after_dispose(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                clientConnectionOptions: new ConnectionOptions { ConnectTimeout = TimeSpan.FromSeconds(1) })
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        await sut.Client.DisposeAsync();

        // Act/Assert
        Assert.That(async () => await sut.Client.ConnectAsync(default), Throws.TypeOf<ObjectDisposedException>());
    }

    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Connect_fails_if_shutdown_is_canceled(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        Task connectTask = sut.Client.ConnectAsync(default);
        using var cts = new CancellationTokenSource();
        _ = sut.Client.ShutdownAsync(cts.Token);

        // Act
        cts.Cancel();

        // Assert
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(async () => await connectTask);
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.OperationAborted));
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

        // Act
        Task shutdownTask = (closeClientSide ? sut.Client : sut.Server).ShutdownAsync();

        // Assert
        Assert.That(async () => await shutdownTask, Throws.Nothing);
    }

    /// <summary>Ensure that ShutdownAsync fails with ConnectionException(ConnectionErrorCode.TransportError) if
    /// ConnectAsync fails with a transport error.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Shutdown_fails_if_connect_fails(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        Task connectTask = sut.Client.ConnectAsync(default);

        // Act
        Task shutdownTask = sut.Client.ShutdownAsync();

        // Assert
        await sut.DisposeListenerAsync(); // dispose the listener to trigger the connection establishment failure.
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(async () => await shutdownTask);
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.TransportError));
        Assert.That(exception!.InnerException, Is.InstanceOf<TransportException>());
    }

    /// <summary>Ensure that ShutdownAsync fails with ConnectionException(ConnectionErrorCode.OperationAborted) if
    /// ConnectAsync is canceled.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Shutdown_fails_if_connect_is_canceled(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(protocol)
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        using var cts = new CancellationTokenSource();
        Task connectTask = sut.Client.ConnectAsync(cts.Token);

        // Act
        Task shutdownTask = sut.Client.ShutdownAsync();
        cts.Cancel();

        // Assert
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(async () => await shutdownTask);
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.OperationAborted));
    }

    /// <summary>Ensure that ShutdownAsync fails with ConnectionException(ConnectionErrorCode.OperationAborted) if
    /// ConnectAsync timeouts.</summary>
    [Test, TestCaseSource(nameof(Protocols))]
    public async Task Shutdown_fails_on_connect_timeout(Protocol protocol)
    {
        // Arrange
        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(
                protocol,
                clientConnectionOptions: new ConnectionOptions { ConnectTimeout = TimeSpan.FromSeconds(1) })
            .BuildServiceProvider(validateScopes: true);
        ClientServerProtocolConnection sut = provider.GetRequiredService<ClientServerProtocolConnection>();

        Task connectTask = sut.Client.ConnectAsync(default);

        // Act
        Task shutdownTask = sut.Client.ShutdownAsync();

        // Act/Assert
        ConnectionException? exception = Assert.ThrowsAsync<ConnectionException>(async () => await shutdownTask);
        Assert.That(exception!.ErrorCode, Is.EqualTo(ConnectionErrorCode.OperationAborted));
    }

    /// <summary>Verifies that connection shutdown timeouts after the <see cref="ConnectionOptions.ShutdownTimeout" />
    /// time period.</summary>
    [Test, TestCaseSource(nameof(Protocols_and_client_or_server))]
    public async Task Shutdown_timeout(Protocol protocol, bool closeClientSide)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        IServiceCollection services = new ServiceCollection();
        if (closeClientSide)
        {
            services.AddProtocolTest(
                protocol,
                dispatcher,
                clientConnectionOptions: new ConnectionOptions { ShutdownTimeout = TimeSpan.FromSeconds(1) });
        }
        else
        {
            services.AddProtocolTest(
                protocol,
                dispatcher,
                serverConnectionOptions: new ConnectionOptions { ShutdownTimeout = TimeSpan.FromSeconds(1) });
        }
        await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<ClientServerProtocolConnection>();
        await sut.ConnectAsync();
        using var request = new OutgoingRequest(new ServiceAddress(protocol));
        Task invokeTask = sut.Client.InvokeAsync(request);
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        Task shutdownTask = (closeClientSide ? sut.Client : sut.Server).ShutdownAsync();

        // Assert
        Assert.That(async () => await shutdownTask, Throws.InstanceOf<TimeoutException>());
        Assert.That(invokeTask.IsCompleted, Is.False);

        // TODO: not AAA
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
        using var request = new OutgoingRequest(new ServiceAddress(protocol));
        Task invokeTask = sut.Client.InvokeAsync(request);
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        Task shutdownTask = (closeClientSide ? sut.Client : sut.Server).ShutdownAsync();

        // Assert
        Assert.That(invokeTask.IsCompleted, Is.False);
        Assert.That(shutdownTask.IsCompleted, Is.False);
        dispatcher.ReleaseDispatch();

        Assert.That(async () => await invokeTask, Throws.Nothing);
        Assert.That(async () => await shutdownTask, Throws.Nothing);
    }
}
