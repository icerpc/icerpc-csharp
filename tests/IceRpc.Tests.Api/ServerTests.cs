// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Transports;
using NUnit.Framework;
using System.Collections.Immutable;

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
                Assert.That(server.Endpoint, Is.EqualTo(Server.DefaultEndpoint));
                Assert.That(server.Endpoint.ToString(), Is.EqualTo("icerpc://[::0]"));
            }

            {
                await using var server = new Server
                {
                    Endpoint = "icerpc://foo:10000"
                };

                // A DNS name cannot be used with a server endpoint
                Assert.Throws<NotSupportedException>(() => server.Listen());
            }

            {
                // Listen twice is incorrect
                await using var server = new Server();
                server.Listen();
                Assert.Throws<InvalidOperationException>(() => server.Listen());
            }

            {
                var colocTransport = new ColocTransport();

                await using var server = new Server
                {
                    MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport),
                    Dispatcher = new Greeter(),
                };

                await using var connection = new Connection
                {
                    MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport),
                    RemoteEndpoint = server.Endpoint
                };
                var proxy = GreeterPrx.FromConnection(connection);

                Assert.ThrowsAsync<ConnectionRefusedException>(async () => await proxy.IcePingAsync());
                server.Listen();
                Assert.DoesNotThrowAsync(async () => await proxy.IcePingAsync());
            }

            {
                var colocTransport = new ColocTransport();

                await using var server = new Server
                {
                    MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport),
                };

                await using var connection = new Connection
                {
                    MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport),
                    RemoteEndpoint = server.Endpoint
                };
                var proxy = GreeterPrx.FromConnection(connection);

                server.Listen();

                // Throws ServiceNotFoundException when Dispatcher is null
                Assert.ThrowsAsync<ServiceNotFoundException>(async () => await proxy.IcePingAsync());
            }

            {
                // Cannot add a middleware to a router after calling DispatchAsync

                var router = new Router();
                router.Map<IGreeter>(new Greeter());

                var colocTransport = new ColocTransport();

                await using var server = new Server
                {
                    Dispatcher = router,
                    MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport),
                };

                await using var connection = new Connection
                {
                    MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport),
                    RemoteEndpoint = server.Endpoint
                };
                var proxy = GreeterPrx.FromConnection(connection);
                server.Listen();

                Assert.DoesNotThrow(() => router.Use(next => next)); // still fine
                Assert.DoesNotThrowAsync(async () => await proxy.IcePingAsync());
                Assert.Throws<InvalidOperationException>(() => router.Use(next => next));
            }

            {
                await using var server1 = new Server
                {
                    Endpoint = "icerpc://127.0.0.1:15001"
                };
                server1.Listen();

                Assert.ThrowsAsync<TransportException>(async () =>
                    {
                        await using var server2 = new Server
                        {
                            Endpoint = "icerpc://127.0.0.1:15001"
                        };
                        server2.Listen();
                    });
            }

            {
                var colocTransport = new ColocTransport();

                await using var server1 = new Server
                {
                    MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport),
                };
                server1.Listen();

                Assert.ThrowsAsync<TransportException>(async () =>
                    {
                        await using var server2 = new Server
                        {
                            MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport),
                        };
                        server2.Listen();
                    });
            }

            {
                // Setting Endpoint after calling Listen is not allowed
                await using var server = new Server();
                server.Listen();
                Assert.Throws<InvalidOperationException>(() => server.Endpoint = "icerpc://127.0.0.1:15001");
            }

            {
                // Calling Listen on a disposed server throws ObjectDisposedException
                var server = new Server();
                await server.DisposeAsync();
                Assert.Throws<ObjectDisposedException>(() => server.Listen());
            }
        }

        [TestCase(" :")]
        [TestCase("t:")]
        [TestCase("tcp: ")]
        [TestCase(":tcp")]
        public void Server_InvalidEndpoints(string endpoint) =>
            Assert.Catch<FormatException>(() => new Server { Endpoint = endpoint });

        [Test]
        // When a client cancels a request, the dispatch is canceled.
        public async Task Server_RequestCancelAsync()
        {
            var colocTransport = new ColocTransport();

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
                    return new OutgoingResponse(request);
                }),
                LoggerFactory = LogAttributeLoggerFactory.Instance,
                MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport),
            };

            server.Listen();

            await using var connection = new Connection
            {
                MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport),
                LoggerFactory = LogAttributeLoggerFactory.Instance,
                RemoteEndpoint = server.Endpoint
            };
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
            var colocTransport = new ColocTransport();

            using var dispatchStartSemaphore = new SemaphoreSlim(0);
            using var dispatchContinueSemaphore = new SemaphoreSlim(0);
            await using var server = new Server
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    dispatchStartSemaphore.Release();
                    await dispatchContinueSemaphore.WaitAsync(cancel);
                    return new OutgoingResponse(request);
                }),
                MultiplexedServerTransport = new SlicServerTransport(colocTransport.ServerTransport),
            };

            server.Listen();

            await using var connection = new Connection
            {
                MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport),
                RemoteEndpoint = server.Endpoint
            };

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

            await using var server = new Server
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
            };

            server.Listen();

            await using var connection = new Connection
            {
                SimpleClientTransport = colocTransport.ClientTransport,
                MultiplexedClientTransport = new SlicClientTransport(colocTransport.ClientTransport),
                RemoteEndpoint = serverEndpoint,
                LoggerFactory = LogAttributeLoggerFactory.Instance
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

            // Ensures the client gets a DispatchException with the Ice protocol and OperationCanceledException with
            // the IceRPC protocol.
            if (protocol == Protocol.Ice)
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
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel) =>
                default;
        }
    }
}
