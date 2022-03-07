// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Transports;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests.Api
{
    [Timeout(5000)]
    public class ServerTests
    {
        [Test]
        // When a client cancels a request, the dispatch is canceled.
        public async Task Server_RequestCancelAsync()
        {
            var colocTransport = new ColocTransport();

            using var semaphore = new SemaphoreSlim(0);
            bool waitForCancellation = true;
            await using var server = new Server(new ServerOptions
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
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
                        catch
                        {
                        }
                        Assert.Fail();
                    }
                    return new OutgoingResponse(request);
                }),
                LoggerFactory = LogAttributeLoggerFactory.Instance,
                MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport),
            });

            server.Listen();

            await using var connection = new Connection(new ConnectionOptions
            {
                MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport),
                LoggerFactory = LogAttributeLoggerFactory.Instance,
                RemoteEndpoint = server.Endpoint
            });
            var proxy = ServicePrx.FromConnection(connection, GreeterPrx.DefaultPath);

            using var cancellationSource = new CancellationTokenSource();
            Task task = proxy.IcePingAsync(cancel: cancellationSource.Token);
            await semaphore.WaitAsync(); // Wait for the dispatch

            Assert.That(task.IsCompleted, Is.False);
            cancellationSource.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await task);

            // Now wait for the dispatch cancellation
            await semaphore.WaitAsync();

            // Verify the service still works.
            waitForCancellation = false;
            Assert.DoesNotThrowAsync(async () => await proxy.IcePingAsync());
            Assert.DoesNotThrowAsync(async () => await server.ShutdownAsync());
        }

        [Test]
        public async Task Server_ShutdownAsync()
        {
            var colocTransport = new ColocTransport();

            using var dispatchStartSemaphore = new SemaphoreSlim(0);
            using var dispatchContinueSemaphore = new SemaphoreSlim(0);
            await using var server = new Server(new ServerOptions
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    dispatchStartSemaphore.Release();
                    await dispatchContinueSemaphore.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                }),
                MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport),
            });

            server.Listen();

            await using var connection = new Connection(new ConnectionOptions
            {
                MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport),
                RemoteEndpoint = server.Endpoint
            });

            var proxy = ServicePrx.FromConnection(connection, GreeterPrx.DefaultPath);

            Task task = proxy.IcePingAsync();
            Assert.That(server.ShutdownComplete.IsCompleted, Is.False);
            await dispatchStartSemaphore.WaitAsync(); // Wait for the dispatch

            Task shutdownTask = server.ShutdownAsync();
            Assert.That(server.ShutdownComplete.IsCompleted, Is.False);
            dispatchContinueSemaphore.Release();

            Assert.DoesNotThrowAsync(async () => await shutdownTask);

            Assert.That(server.ShutdownComplete.IsCompleted, Is.True);
        }

        [TestCase(false, "ice")]
        [TestCase(true, "ice")]
        [TestCase(false, "icerpc")]
        [TestCase(true, "icerpc")]
        // [[Log(LogAttributeLevel.Debug)]
        // Canceling the cancellation token (source) of ShutdownAsync results in a DispatchException when the operation
        // completes with an OperationCanceledException. It also test calling DisposeAsync is called instead of
        // shutdown, which call ShutdownAsync with a canceled token.
        public async Task Server_ShutdownCancelAsync(bool disposeInsteadOfShutdown, string protocolStr)
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

            var proxy = ServicePrx.FromConnection(connection, GreeterPrx.DefaultPath);

            Task task = proxy.IcePingAsync();
            Assert.That(server.ShutdownComplete.IsCompleted, Is.False);
            await semaphore.WaitAsync(); // Wait for the dispatch

            Task shutdownTask;
            if (disposeInsteadOfShutdown)
            {
                // Dispose to trigger the dispatch cancellation immediately.
                Assert.That(task.IsCompleted, Is.False);
                shutdownTask = server.DisposeAsync().AsTask();
            }
            else
            {
                // Shutdown and cancel it to trigger the dispatch cancellation.
                using var cancellationSource = new CancellationTokenSource();
                shutdownTask = server.ShutdownAsync(cancellationSource.Token);
                Assert.That(task.IsCompleted, Is.False);
                Assert.That(shutdownTask.IsCompleted, Is.False);
                cancellationSource.Cancel();
            }

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

            Assert.That(server.ShutdownComplete.IsCompleted, Is.True);
        }
    }
}
