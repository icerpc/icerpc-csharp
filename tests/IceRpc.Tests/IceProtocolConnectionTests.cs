// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Internal;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests;

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
            // Service not found failure with a service address that has no server address gets OtherReplica retry policy response
            // field.
            yield return new TestCaseData(
                new ServiceAddress(Protocol.Ice),
                DispatchErrorCode.ServiceNotFound,
                RetryPolicy.OtherReplica);

            // Service not found failure with a service address that has server addresses does not get a retry policy response
            // field
            yield return new TestCaseData(
                new ServiceAddress(new Uri("ice://localhost/service")),
                DispatchErrorCode.ServiceNotFound,
                null);

            // No retry policy field with other dispatch errors
            yield return new TestCaseData(
                new ServiceAddress(Protocol.Ice),
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

        var dispatcher = new InlineDispatcher(async (request, cancel) =>
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
        });

        var services = new ServiceCollection().AddProtocolTest(Protocol.Ice, dispatcher);
        services.AddOptions<ServerOptions>().Configure(options =>
        {
            options.ConnectionOptions.Dispatcher = dispatcher;
            options.ConnectionOptions.MaxIceConcurrentDispatches = maxConcurrentDispatches;
        });
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice));
        var responseTasks = new List<Task<IncomingResponse>>();

        // Act
        for (int i = 0; i < maxConcurrentDispatches + 1; ++i)
        {
            responseTasks.Add(sut.Client.InvokeAsync(request));
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

    /// <summary>Verifies that when dispatches are blocked waiting for the dispatch semaphore that aborting the server
    /// connection correctly cancels the dispatch semaphore wait. If the dispatch semaphore wait wasn't canceled, the
    /// DisposeAsync call would hang because it waits for the read semaphore to be released.</summary>
    [Test]
    public async Task Connection_with_dispatches_waiting_for_concurrent_dispatch_unblocks_on_dispose()
    {
        // Arrange
        int dispatchCount = 0;
        var dispatcher = new InlineDispatcher(
            async (request, cancel) =>
            {
                ++dispatchCount;
                try
                {
                    // Wait for the dispatch to be canceled by DisposeAsync
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancel);
                }
                catch
                {
                }
                return new OutgoingResponse(request);
            });

        var services = new ServiceCollection().AddProtocolTest(Protocol.Ice);
        services.AddOptions<ServerOptions>().Configure(options =>
        {
            options.ConnectionOptions.Dispatcher = dispatcher;
            options.ConnectionOptions.MaxIceConcurrentDispatches = 1;
        });
        await using var provider = services.BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Perform two invocations. The first blocks so the second won't be dispatched. It will block on the dispatch
        // semaphore which is canceled on dispose.
        Task<IncomingResponse> invokeTask = sut.Client.InvokeAsync(
            new OutgoingRequest(new ServiceAddress(Protocol.Ice)));
        _ = sut.Client.InvokeAsync(
            new OutgoingRequest(new ServiceAddress(Protocol.Ice)));

        // Make sure the second request is received and blocked on the dispatch semaphore.
        await Task.Delay(200);

        // Act
        await sut.Server.DisposeAsync();

        // Assert
        Assert.That(dispatchCount, Is.EqualTo(1));
    }

    /// <summary>Verifies that disposing a server connection causes the invocation to fail with <see
    /// cref="DispatchException"/>.</summary>
    [Test]
    public async Task Disposing_server_connection_triggers_dispatch_exception([Values(false, true)] bool shutdown)
    {
        // Arrange
        using var dispatcher = new TestDispatcher();

        await using ServiceProvider provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);
        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice));
        var invokeTask = sut.Client.InvokeAsync(request);
        await dispatcher.DispatchStart; // Wait for the dispatch to start

        // Act
        if (shutdown)
        {
            _ = sut.Server.ShutdownAsync("");
        }
        await sut.Server.DisposeAsync();

        // Assert
        var ex = Assert.ThrowsAsync<DispatchException>(
            async () =>
            {
                IncomingResponse response = await invokeTask;
                throw await response.DecodeFailureAsync(request, new ServiceProxy(sut.Client));
            });
        // TODO: should this be DispatchErrorCode.ConnectionAborted instead of DispatchErrorCode.Canceled to match the
        // icerpc stream error code? Right now, we don't include the error code in the message. Just the following
        // message.
        Assert.That(ex!.Message, Is.EqualTo("dispatch canceled by peer"));
    }

    /// <summary>Verifies that a failure response contains the expected retry policy field.</summary>
    [Test, TestCaseSource(nameof(DispatchExceptionRetryPolicySource))]
    public async Task Dispatch_failure_response_contain_the_expected_retry_policy_field(
        ServiceAddress serviceAddress,
        DispatchErrorCode errorCode,
        RetryPolicy? expectedRetryPolicy)
    {
        // Arrange
        var dispatcher = new InlineDispatcher(
            (request, cancel) => throw new DispatchException(errorCode: errorCode));

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var request = new OutgoingRequest(serviceAddress);

        // Act
        var response = await sut.Client.InvokeAsync(request);

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

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();
        var request = new OutgoingRequest(new ServiceAddress(Protocol.Ice));

        // Act
        var response = await sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        var exception = await response.DecodeFailureAsync(request, new ServiceProxy(sut.Client)) as DispatchException;
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception!.ErrorCode, Is.EqualTo(errorCode));
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

        await using var provider = new ServiceCollection()
            .AddProtocolTest(Protocol.Ice, dispatcher)
            .BuildServiceProvider(validateScopes: true);

        var sut = provider.GetRequiredService<IClientServerProtocolConnection>();
        await sut.ConnectAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new ServiceAddress(Protocol.Ice)));

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }
}
