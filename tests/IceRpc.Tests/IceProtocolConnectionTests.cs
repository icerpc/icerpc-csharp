// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Internal;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests;

[Timeout(5000)]
[Parallelizable(ParallelScope.All)]
public sealed class IceProtocolConnectionTests
{
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

        Assert.Multiple(async () =>
        {
            await Task.WhenAll(responseTasks);
            Assert.That(maxCount, Is.EqualTo(maxConcurrentDispatches));
        });
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
        await using var sut = await serviceProvider.GetClientServerProtocolConnectionAsync();
        _ = sut.Server.AcceptRequestsAsync();

        // Act
        _ = sut.Client.InvokeAsync(new OutgoingRequest(new Proxy(Protocol.Ice)));

        // Assert
        Assert.That(await payloadStreamDecorator.Completed, Is.InstanceOf<NotSupportedException>());
    }
}
