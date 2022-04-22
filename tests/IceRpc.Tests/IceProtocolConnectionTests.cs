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
    public static IEnumerable<TestCaseData> MiddlewareExceptionIsEncodedAsADispatchExceptionSource
    {
        get
        {
            yield return new TestCaseData(new OperationCanceledException(), DispatchErrorCode.Canceled);
            yield return new TestCaseData(new InvalidDataException("invalid data"), DispatchErrorCode.InvalidData);
            yield return new TestCaseData(new MyException(), DispatchErrorCode.UnhandledException);
            yield return new TestCaseData(new InvalidOperationException(), DispatchErrorCode.UnhandledException);
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

        var serverConnectionOptions = new ConnectionOptions
        {
            Dispatcher = new InlineDispatcher(async (request, cancel) =>
            {
                await request.Payload.CompleteAsync(); // done with payload

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

            IceProtocolOptions = new IceProtocolOptions { MaxConcurrentDispatches = maxConcurrentDispatches }
        };

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.Ice)
            .UseServerConnectionOptions(serverConnectionOptions)
            .BuildServiceProvider();

        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Server.AcceptRequestsAsync();
        _ = sut.Client.AcceptRequestsAsync();

        var request = new OutgoingRequest(new Proxy(Protocol.Ice));
        var responseTasks = new List<Task<IncomingResponse>>();

        // Act
        for (int i = 0; i < maxConcurrentDispatches + 1; ++i)
        {
            responseTasks.Add(sut.Client.InvokeAsync(request, default));
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

    [Test]
    public async Task Connection_shutdown_cancels_invocations()
    {
        // Arrange
        using var start = new SemaphoreSlim(0);
        using var hold = new SemaphoreSlim(0);

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.Ice)
            .UseServerConnectionOptions(new ConnectionOptions()
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    start.Release();
                    await hold.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                })
            })
            .BuildServiceProvider();

        var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Client.AcceptRequestsAsync();
        _ = sut.Server.AcceptRequestsAsync();
        var invokeTask = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.Ice)));
        await start.WaitAsync(); // Wait for the dispatch to start

        // Act
        await sut.Client.ShutdownAsync("", default);

        // Assert
        Assert.That(async () => await invokeTask, Throws.TypeOf<OperationCanceledException>());

        hold.Release();
    }

    /// <summary>Verifies that with the ice protocol, when a middleware throws a Slice exception other than a
    /// DispatchException, we encode a DispatchException with the expected error code.</summary>
    [Test, TestCaseSource(nameof(MiddlewareExceptionIsEncodedAsADispatchExceptionSource))]
    public async Task Middleware_exception_is_encoded_as_a_dispatch_exception(
        Exception thrownException,
        DispatchErrorCode errorCode)
    {
        var dispatcher = new InlineDispatcher((request, cancel) =>
        {
            throw thrownException;
        });

        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.Ice)
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();

        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Server.AcceptRequestsAsync();
        _ = sut.Client.AcceptRequestsAsync();

        // Act
        var response = await sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.Ice)));

        // Assert
        Assert.That(response.ResultType, Is.EqualTo(ResultType.Failure));
        var exception = await response.DecodeFailureAsync() as DispatchException;
        Assert.That(exception, Is.Not.Null);
        Assert.That(exception.ErrorCode, Is.EqualTo(errorCode));
    }

    /// <summary>Ensures that the request payload stream is completed even if the Ice protocol doesn't support
    /// it.</summary>
    [Test]
    public async Task PayloadStream_completed_on_request()
    {
        // Arrange
        await using var serviceProvider = new ProtocolServiceCollection()
            .UseProtocol(Protocol.Ice)
            .BuildServiceProvider();
        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();

        var payloadStreamDecorator = new PayloadPipeReaderDecorator(EmptyPipeReader.Instance);
        var request = new OutgoingRequest(new Proxy(Protocol.Ice))
        {
            PayloadStream = payloadStreamDecorator
        };

        // Act
        _ = sut.Client.InvokeAsync(request);

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.InstanceOf<NotSupportedException>());
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
            .UseServerConnectionOptions(new ConnectionOptions() { Dispatcher = dispatcher })
            .BuildServiceProvider();
        await using var clientServerProtocolConnection = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = clientServerProtocolConnection.Server.AcceptRequestsAsync();

        // Act
        _ = clientServerProtocolConnection.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.Ice)));

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }
}
