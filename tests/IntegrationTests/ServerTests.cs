// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Tests;
using IceRpc.Transports;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IntegrationTests;

[Parallelizable(ParallelScope.All)]
[Timeout(5000)]
public class ServerTests
{
    /// <summary>Verifies that canceling an invocation also cancels the corresponding server dispatch.</summary>
    [Test]
    public async Task Canceling_a_request_also_cancels_the_dispatch()
    {
        using var semaphore = new SemaphoreSlim(0);
        bool waitForCancellation = true;

        await using var serviceProvider = new IntegrationTestServiceCollection().AddScoped<IDispatcher>(_ =>
            new InlineDispatcher(async (request, cancel) =>
            {
                if (waitForCancellation)
                {
                    Assert.That(cancel.CanBeCanceled, Is.True);
                    semaphore.Release();
                    try
                    {
                        await Task.Delay(-1, cancel);
                    }
                    catch (OperationCanceledException)
                    {
                        semaphore.Release();
                        throw;
                    }
                    Assert.Fail();
                }
                return new OutgoingResponse(request);
            })).BuildServiceProvider();

        var server = serviceProvider.GetRequiredService<Server>();

        var proxy = new ServicePrx(serviceProvider.GetRequiredService<Proxy>());

        using var cancellationSource = new CancellationTokenSource();
        Task task = proxy.IcePingAsync(cancel: cancellationSource.Token);
        await semaphore.WaitAsync(); // Wait for the dispatch

        Assert.That(task.IsCompleted, Is.False);
        cancellationSource.Cancel();
        Assert.CatchAsync<OperationCanceledException>(async () => await task);

        // Now wait for the dispatch cancellation
        await semaphore.WaitAsync();
    }

    /// <summary>Verifies that shutting down the server does not complete until the pending dispatches have finished.
    /// </summary>
    // TODO rework this test as suggested in https://github.com/zeroc-ice/icerpc-csharp/pull/880#discussion_r821539747
    [Test]
    public async Task Shutting_down_the_server_waits_for_pending_dispatch_to_finish()
    {
        using var dispatchStartSemaphore = new SemaphoreSlim(0);
        using var dispatchContinueSemaphore = new SemaphoreSlim(0);

        await using var serviceProvider = new IntegrationTestServiceCollection().AddScoped<IDispatcher>(_ =>
            new InlineDispatcher(async (request, cancel) =>
            {
                dispatchStartSemaphore.Release();
                await dispatchContinueSemaphore.WaitAsync(cancel);
                return new OutgoingResponse(request);
            })).BuildServiceProvider();

        var server = serviceProvider.GetRequiredService<Server>();

        var proxy = new ServicePrx(serviceProvider.GetRequiredService<Proxy>());

        Task task = proxy.IcePingAsync();
        Assert.That(server.ShutdownComplete.IsCompleted, Is.False);
        await dispatchStartSemaphore.WaitAsync(); // Wait for the dispatch

        Task shutdownTask = server.ShutdownAsync();
        await Task.Delay(100); // Ensure that shutdown cannot complete while there is a pending dispatch.
        Assert.That(server.ShutdownComplete.IsCompleted, Is.False);
        dispatchContinueSemaphore.Release();

        Assert.DoesNotThrowAsync(async () => await shutdownTask);
    }

    /// <summary>Verifies that canceling the server shutdown triggers the cancellation of the pending invocations,
    /// and the corresponding server dispatch.</summary>
    /// <param name="protocolStr">The protocol used for the tests.</param>
    [TestCase("ice")]
    [TestCase("icerpc")]
    public async Task Canceling_server_shutdown(string protocolStr)
    {
        var colocTransport = new ColocTransport();
        var protocol = Protocol.FromString(protocolStr);
        Assert.That(protocol.IsSupported, Is.True);

        using var semaphore = new SemaphoreSlim(0);
        var serverEndpoint = new Endpoint(protocol)
        {
            Params = ImmutableDictionary<string, string>.Empty.Add("transport", ColocTransport.Name)
        };

        await using var server = new Server(new ServerOptions
        {
            Dispatcher = new InlineDispatcher(async (request, cancel) =>
            {
                Assert.That(cancel.CanBeCanceled, Is.True);
                semaphore.Release();
                await Task.Delay(-1, cancel);
                return new OutgoingResponse(request);
            }),
            Endpoint = serverEndpoint,
            SimpleServerTransport = colocTransport.ServerTransport,
            MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport),
            LoggerFactory = LogAttributeLoggerFactory.Instance
        });

        server.Listen();

        await using var connection = new Connection(new ConnectionOptions
        {
            SimpleClientTransport = colocTransport.ClientTransport,
            MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport),
            RemoteEndpoint = serverEndpoint,
            LoggerFactory = LogAttributeLoggerFactory.Instance
        });

        var proxy = ServicePrx.FromConnection(connection);

        Task task = proxy.IcePingAsync();
        Assert.That(server.ShutdownComplete.IsCompleted, Is.False);
        await semaphore.WaitAsync(); // Wait for the dispatch

        // Shutdown and cancel it to trigger the dispatch cancellation.
        using var cancellationSource = new CancellationTokenSource();
        Task shutdownTask = server.ShutdownAsync(cancellationSource.Token);
        Assert.That(task.IsCompleted, Is.False);
        await Task.Delay(100);  // Ensure that shutdown cannot complete while there is a pending dispatch.
        Assert.That(shutdownTask.IsCompleted, Is.False);
        cancellationSource.Cancel();

        // Ensures the client gets a DispatchException with the ice protocol and OperationCanceledException with
        // the icerpc protocol.
        if (protocol == Protocol.Ice)
        {
            var dispatchException = Assert.ThrowsAsync<DispatchException>(() => task);
            Assert.That(dispatchException!.ErrorCode, Is.EqualTo(DispatchErrorCode.Canceled));
        }
        else
        {
            Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
        }

        // Shutdown shouldn't throw.
        Assert.DoesNotThrowAsync(() => shutdownTask);
    }
}
