// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests;

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public sealed class IceProtocolConnectionTests
{
    public static IEnumerable<TestCaseData> ExceptionIsEncodedAsADispatchExceptionSource
    {
        get
        {
            yield return new TestCaseData(new OperationCanceledException(), DispatchErrorCode.Canceled);
            yield return new TestCaseData(new InvalidDataException("invalid data"), DispatchErrorCode.InvalidData);
            yield return new TestCaseData(new MyException(), DispatchErrorCode.UnhandledException);
            yield return new TestCaseData(new InvalidOperationException(), DispatchErrorCode.UnhandledException);
        }
    }

    public static IEnumerable<TestCaseData> DispatchExceptionRetryPolicySource
    {
        get
        {
            // Service not found failure with endpointless proxy gets OtherReplica retry policy response field.
            yield return new TestCaseData(
                new Proxy(Protocol.Ice),
                DispatchErrorCode.ServiceNotFound,
                RetryPolicy.OtherReplica);

            // Service not found failure with a proxy that has endpoints does not get a retry policy response field
            yield return new TestCaseData(
                Proxy.Parse("ice://localhost/service"),
                DispatchErrorCode.ServiceNotFound,
                null);

            // No retry policy field with other dispatch errors
            yield return new TestCaseData(
                new Proxy(Protocol.Ice),
                DispatchErrorCode.UnhandledException,
                null);
        }
    }

    /// <summary>Verifies that concurrent dispatches on a given ice connection are limited to MaxConcurrentDispatches.
    /// </summary>
    [Test]
    public async Task Connection_dispatches_requests_concurrently_up_to_max_concurrent_dispatches(
        [Values(1, 200)] int maxConcurrentDispatches)
    {
        // Arrange
        using var startSemaphore = new SemaphoreSlim(0);
        using var workSemaphore = new SemaphoreSlim(0);
        int count = 0;
        int maxCount = 0;
        var mutex = new object();

        var serverOptions = new ServerOptions
        {
            Dispatcher = new InlineDispatcher(async (request, cancel) =>
            {
                // We want to make sure that no more than maxConcurrentDispatches are executing this dispatcher. So
                // we are tracking the maximum count here (before work) and decrement this count immediately in the
                // "work". Without the decrement, the count (and max count) could be greater than
                // maxConcurrentDispatches.
                IncrementCount();
                startSemaphore.Release();
                await workSemaphore.WaitAsync(cancel);
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
            }),

            IceServerOptions = new() { MaxConcurrentDispatches = maxConcurrentDispatches }
        };

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.Ice)
            .UseServerOptions(serverOptions)
            .BuildServiceProvider();

        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        var request = new OutgoingRequest(new Proxy(Protocol.Ice));
        var responseTasks = new List<Task<IncomingResponse>>();

        // Act
        for (int i = 0; i < maxConcurrentDispatches + 1; ++i)
        {
            responseTasks.Add(sut.Client.InvokeAsync(request, InvalidConnection.Ice, default));
        }
        // wait for maxDispatchesPerConnection dispatches to start
        for (int i = 0; i < maxConcurrentDispatches; ++i)
        {
            await startSemaphore.WaitAsync();
        }

        // Assert
        for (int i = 0; i < maxConcurrentDispatches + 1; ++i)
        {
            Assert.That(responseTasks[i].IsCompleted, Is.False);
        }

        workSemaphore.Release(maxConcurrentDispatches + 1);

        await Task.WhenAll(responseTasks);
        Assert.That(maxCount, Is.EqualTo(maxConcurrentDispatches));
    }

    /// <summary>Verifies that when dispatches are blocked waiting for the dispatch semaphore that disposing the server
    /// connection correctly cancels the dispatch semaphore wait. If the dispatch semaphore wait wasn't canceled, the
    /// DisposeAsync call would hang because it waits for the read semaphore to be released.</summary>
    /// </summary>
    [Test]
    public async Task Connection_with_dispatches_waiting_for_concurrent_dispatch_unblocks_on_dispose()
    {
        // Arrange
        using var semaphore = new SemaphoreSlim(0);
        int dispatchCount = 0;
        var serverOptions = new ServerOptions
        {
            Dispatcher = new InlineDispatcher(
                async (request, cancel) =>
                {
                    ++dispatchCount;
                    await semaphore.WaitAsync(CancellationToken.None);
                    return new OutgoingResponse(request);
                }),
            IceServerOptions = new() { MaxConcurrentDispatches = 1 }
        };

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.Ice)
            .UseServerOptions(serverOptions)
            .BuildServiceProvider();

        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        // Perform two invocations. The first blocks so the second won't be dispatched. It will block on the dispatch
        // semaphore.
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.Ice)), InvalidConnection.Ice, default);
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.Ice)), InvalidConnection.Ice, default);

        // Make sure the second request is received and blocked on the dispatch semaphore.
        await Task.Delay(200);

        // Act
        await sut.Server.DisposeAsync();

        // Assert
        Assert.That(dispatchCount, Is.EqualTo(1));

        semaphore.Release();
    }

    /// <summary>Verifies that a failure response contains the expected retry policy field.</summary>
    [Test, TestCaseSource(nameof(DispatchExceptionRetryPolicySource))]
    public async Task Dispatch_failure_response_contain_the_expected_retry_policy_field(
        Proxy proxy,
        DispatchErrorCode errorCode,
        RetryPolicy? expectedRetryPolicy)
    {
        // Arrange
        var dispatcher = new InlineDispatcher(
            (request, cancel) => throw new DispatchException(errorCode: errorCode));

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.Ice)
            .UseServerOptions(new ServerOptions { Dispatcher = dispatcher })
            .BuildServiceProvider();

        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        var request = new OutgoingRequest(proxy);

        // Act
        var response = await sut.Client.InvokeAsync(request, InvalidConnection.Ice);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        var retryPolicy = response.Fields.DecodeValue(
                ResponseFieldKey.RetryPolicy,
                (ref SliceDecoder decoder) => new RetryPolicy(ref decoder));
        Assert.That(retryPolicy, Is.EqualTo(expectedRetryPolicy));
    }

    /// <summary>Verifies that with the ice protocol, when a exception other than a DispatchException is thrown
    /// during the dispatch, we encode a DispatchException with the expected error code.</summary>
    [Test, TestCaseSource(nameof(ExceptionIsEncodedAsADispatchExceptionSource))]
    public async Task Exception_is_encoded_as_a_dispatch_exception(
        Exception thrownException,
        DispatchErrorCode errorCode)
    {
        var dispatcher = new InlineDispatcher((request, cancel) => throw thrownException);

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.Ice)
            .UseServerOptions(new ServerOptions { Dispatcher = dispatcher })
            .BuildServiceProvider();

        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        var request = new OutgoingRequest(new Proxy(Protocol.Ice));

        // Act
        var response = await sut.Client.InvokeAsync(request, InvalidConnection.Ice);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        var exception = await response.DecodeFailureAsync(request) as DispatchException;
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception.ErrorCode, Is.EqualTo(errorCode));
    }

    /// <summary>Ensures that the response payload stream is completed even if the Ice protocol doesn't support
    /// it.</summary>
    [Test]
    public async Task PayloadStream_completed_on_response()
    {
        // Arrange
        var payloadStreamDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var dispatcher = new InlineDispatcher((request, cancel) =>
                new(new OutgoingResponse(request)
                {
                    PayloadStream = payloadStreamDecorator
                }));

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.Ice)
            .UseServerOptions(new ServerOptions { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var clientServerProtocolConnection = await serviceProvider.GetClientServerProtocolConnectionAsync();

        // Act
        _ = clientServerProtocolConnection.Client.InvokeAsync(
            new OutgoingRequest(new Proxy(Protocol.Ice)),
            InvalidConnection.Ice);

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }

    /// <summary>With ice protocol the connection shutdown triggers the cancellation of invocations. This is different
    /// with IceRpc see <see cref="IceRpcProtocolConnectionTests.Shutdown_waits_for_pending_invocations_to_finish"/>.
    /// </summary>
    [Test]
    public async Task Shutdown_cancels_invocations()
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.Ice)
            .UseServerOptions(new ServerOptions
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                })
            })
            .BuildServiceProvider();

        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        sut.Server.PeerShutdownInitiated = message =>
            sut.Server.ShutdownAsync("");

        var invokeTask = sut.Client.InvokeAsync(
            new OutgoingRequest(new Proxy(Protocol.Ice)),
            InvalidConnection.Ice);

        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        _ = sut.Client.ShutdownAsync("");

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<OperationCanceledException>());

        hold.Release();
    }
}
