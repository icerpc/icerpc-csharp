// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Timeout(5000)]
    public class ServerTests
    {
        [Test]
        public async Task Server_Exceptions()
        {
            {
                await using var server = new Server();
                Assert.AreEqual(Endpoint.FromString("ice+tcp://[::0]"), server.Endpoint);
            }

            {
                await using var server = new Server
                {
                    Endpoint = "ice+tcp://foo:10000"
                };

                // A DNS name cannot be used with a server endpoint
                Assert.Throws<NotSupportedException>(() => server.Listen());
            }

            {
                // Listen twice is incorrect
                await using var server = new Server
                {
                    Endpoint = TestHelper.GetUniqueColocEndpoint()
                };
                server.Listen();
                Assert.Throws<InvalidOperationException>(() => server.Listen());
            }

            {
                await using var server = new Server
                {
                    Dispatcher = new Greeter(),
                    Endpoint = TestHelper.GetUniqueColocEndpoint()
                };

                await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
                var proxy = GreeterPrx.FromConnection(connection);

                Assert.ThrowsAsync<ConnectionRefusedException>(async () => await proxy.IcePingAsync());
                server.Listen();
                Assert.DoesNotThrowAsync(async () => await proxy.IcePingAsync());
            }

            {
                await using var server = new Server
                {
                    Endpoint = TestHelper.GetUniqueColocEndpoint()
                };

                await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
                var proxy = GreeterPrx.FromConnection(connection);

                server.Listen();

                // Throws ServiceNotFoundException when Dispatcher is null
                Assert.ThrowsAsync<ServiceNotFoundException>(async () => await proxy.IcePingAsync());
            }

            {
                // Cannot add a middleware to a router after calling DispatchAsync

                var router = new Router();
                router.Map<IGreeter>(new Greeter());

                await using var server = new Server
                {
                    Dispatcher = router,
                    Endpoint = TestHelper.GetUniqueColocEndpoint()
                };

                await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
                var proxy = GreeterPrx.FromConnection(connection);
                server.Listen();

                Assert.DoesNotThrow(() => router.Use(next => next)); // still fine
                Assert.DoesNotThrowAsync(async () => await proxy.IcePingAsync());
                Assert.Throws<InvalidOperationException>(() => router.Use(next => next));
            }

            {
                await using var server1 = new Server
                {
                    Endpoint = "ice+tcp://127.0.0.1:15001"
                };
                server1.Listen();

                Assert.ThrowsAsync<TransportException>(async () =>
                    {
                        await using var server2 = new Server
                        {
                            Endpoint = "ice+tcp://127.0.0.1:15001"
                        };
                        server2.Listen();
                    });
            }

            {
                Endpoint endpoint = TestHelper.GetUniqueColocEndpoint();
                await using var server1 = new Server
                {
                    Endpoint = endpoint
                };
                server1.Listen();

                Assert.ThrowsAsync<TransportException>(async () =>
                    {
                        await using var server2 = new Server
                        {
                            Endpoint = endpoint
                        };
                        server2.Listen();
                    });
            }

            {
                // Calling Listen on a disposed server throws ObjectDisposedException
                var server = new Server
                {
                    Endpoint = TestHelper.GetUniqueColocEndpoint()
                };
                await server.DisposeAsync();
                Assert.Throws<ObjectDisposedException>(() => server.Listen());
            }
        }

        [TestCase(" :")]
        [TestCase("t:")]
        [TestCase("tcp: ")]
        [TestCase(":tcp")]
        public void Server_InvalidEndpoints(string endpoint) =>
            Assert.Throws<FormatException>(() => new Server { Endpoint = endpoint });

        [Test]
        // When a client cancels a request, the dispatch is canceled.
        public async Task Server_RequestCancelAsync()
        {
            using var semaphore = new SemaphoreSlim(0);
            bool waitForCancellation = true;
            await using var server = new Server
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
                    return OutgoingResponse.ForPayload(request, default);
                }),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.Endpoint };
            var proxy = GreeterPrx.FromConnection(connection);

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
            using var dispatchStartSemaphore = new SemaphoreSlim(0);
            using var dispatchContinueSemaphore = new SemaphoreSlim(0);
            await using var server = new Server
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    dispatchStartSemaphore.Release();
                    await dispatchContinueSemaphore.WaitAsync(cancel);
                    return OutgoingResponse.ForPayload(request, default);
                }),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.Endpoint };

            var proxy = GreeterPrx.FromConnection(connection);

            Task task = proxy.IcePingAsync();
            Assert.That(server.ShutdownComplete.IsCompleted, Is.False);
            await dispatchStartSemaphore.WaitAsync(); // Wait for the dispatch

            Task shutdownTask = server.ShutdownAsync();
            Assert.That(server.ShutdownComplete.IsCompleted, Is.False);
            dispatchContinueSemaphore.Release();

            Assert.DoesNotThrowAsync(async () => await shutdownTask);

            Assert.That(server.ShutdownComplete.IsCompleted, Is.True);
        }

        [TestCase(false, ProtocolCode.Ice1)]
        [TestCase(true, ProtocolCode.Ice1)]
        [TestCase(false, ProtocolCode.Ice2)]
        [TestCase(true, ProtocolCode.Ice2)]
        // Canceling the cancellation token (source) of ShutdownAsync results in a DispatchException when the operation
        // completes with an OperationCanceledException. It also test calling DisposeAsync is called instead of
        // shutdown, which call ShutdownAsync with a canceled token.
        public async Task Server_ShutdownCancelAsync(bool disposeInsteadOfShutdown, ProtocolCode protocol)
        {
            using var semaphore = new SemaphoreSlim(0);
            Endpoint serverEndpoint = TestHelper.GetUniqueColocEndpoint(Protocol.FromProtocolCode(protocol));
            await using var server = new Server
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    Assert.That(cancel.CanBeCanceled, Is.True);
                    semaphore.Release();
                    await Task.Delay(-1, cancel);
                    return OutgoingResponse.ForPayload(request, default);
                }),
                Endpoint = serverEndpoint
            };

            server.Listen();

            await using var connection = new Connection
            {
                RemoteEndpoint = serverEndpoint
            };

            var proxy = GreeterPrx.FromConnection(connection);

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

            // Ensure the client gets a DispatchException with Ice1 and OperationCanceledException with Ice2.
            if (protocol == ProtocolCode.Ice1)
            {
                Assert.ThrowsAsync<DispatchException>(async () => await task);
            }
            else
            {
                Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
            }
            // Shutdown shouldn't throw.
            Assert.DoesNotThrowAsync(async () => await shutdownTask);

            Assert.That(server.ShutdownComplete.IsCompleted, Is.True);
        }

        private class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                default;
        }
    }
}
