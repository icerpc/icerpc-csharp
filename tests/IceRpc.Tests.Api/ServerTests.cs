// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    [Timeout(5000)]
    public class ServerTests
    {
        [Test]
        public async Task Server_Exceptions()
        {
            {
                await using var server = new Server
                {
                    Endpoint = "tcp -h foo -p 10000"
                };

                // A DNS name cannot be used with a server endpoint
                Assert.Throws<NotSupportedException>(() => server.Listen());

                // HostName can't be empty
                Assert.Throws<ArgumentException>(() => server.HostName = "");

                Assert.DoesNotThrow(() => ServicePrx.FromServer(server, "/foo"));
                server.Endpoint = null;
                Assert.Throws<InvalidOperationException>(() => ServicePrx.FromServer(server, "/foo"));
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

                await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };
                var proxy = GreeterPrx.FromConnection(connection);

                Assert.ThrowsAsync<ConnectionRefusedException>(async () => await proxy.IcePingAsync());
                server.Listen();
                Assert.DoesNotThrowAsync(async () => await proxy.IcePingAsync());

                // Throws ServiceNotFoundException when Dispatcher is null
                server.Dispatcher = null;
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

                await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };
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
                string endpoint = TestHelper.GetUniqueColocEndpoint();
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
                await using var server = new Server
                {
                    Endpoint = "ice+tcp://127.0.0.1:15001"
                };

                server.Listen();

                await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };

                var prx = ServicePrx.FromConnection(connection);

                IDispatcher dispatcher = new Greeter();

                // We can set Dispatcher on a client connection
                Assert.DoesNotThrow(() => connection.Dispatcher = dispatcher);
                Assert.DoesNotThrow(() => connection.Dispatcher = null);
            }
        }

        [TestCase("ice+tcp://127.0.0.1:0?tls=false")]
        [TestCase("tcp -h 127.0.0.1 -p 0 -t 15000")]
        public async Task Server_EndpointInformation(string endpoint)
        {
            await using var server = new Server
            {
                Endpoint = endpoint
            };

            Assert.AreEqual(Dns.GetHostName().ToLowerInvariant(), server.ProxyEndpoint?.Host);
            server.HostName = "localhost";
            Assert.AreEqual("localhost", server.ProxyEndpoint?.Host);

            server.Listen();

            Assert.That(server.Endpoint, Is.Not.Null);
            Assert.AreEqual(Transport.TCP, server.Endpoint.Transport);
            Assert.AreEqual("127.0.0.1", server.Endpoint.Host);
            Assert.That(server.Endpoint.Port, Is.GreaterThan(0));

            if (server.Endpoint.Protocol == Protocol.Ice1)
            {
                Assert.AreEqual("15000", server.Endpoint["timeout"]);
            }

            Assert.That(server.ProxyEndpoint, Is.Not.Null);
            Assert.AreEqual(Transport.TCP, server.ProxyEndpoint!.Transport);
            Assert.AreEqual("localhost", server.ProxyEndpoint.Host);
            Assert.AreEqual(server.Endpoint.Port, server.ProxyEndpoint.Port);

            if (server.ProxyEndpoint.Protocol == Protocol.Ice1)
            {
                Assert.AreEqual("15000", server.ProxyEndpoint["timeout"]);
            }
        }

        [Test]
        public async Task Server_EndpointAsync()
        {
            // Verifies that changing Endpoint or HostName updates ProxyEndpoint.

            await using var server = new Server();

            Assert.That(server.ProxyEndpoint, Is.Null);
            server.Endpoint = "ice+tcp://127.0.0.1";
            Assert.AreEqual(server.Endpoint.ToString().Replace("127.0.0.1",
                                                               server.HostName,
                                                               StringComparison.InvariantCulture),
                            server.ProxyEndpoint!.ToString());
            server.HostName = "foobar";
            Assert.AreEqual(server.Endpoint.ToString().Replace("127.0.0.1",
                                                               server.HostName,
                                                               StringComparison.InvariantCulture),
                            server.ProxyEndpoint.ToString());

            // Verifies that changing Endpoint updates Protocol
            Assert.AreEqual(Protocol.Ice2, server.Protocol);
            server.Endpoint = "tcp -h 127.0.0.1 -p 0";
            Assert.AreEqual(Protocol.Ice1, server.Protocol);
            server.Endpoint = null;
            Assert.AreEqual(Protocol.Ice2, server.Protocol);
        }

        [TestCase(" :")]
        [TestCase("tcp: ")]
        [TestCase(":tcp")]
        public void Server_InvalidEndpoints(string endpoint) =>
            Assert.Throws<FormatException>(() => new Server { Endpoint = endpoint });

        [Test]
        [Log(LogAttributeLevel.Debug)]
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
                    return new OutgoingResponse(request, Payload.FromVoidReturnValue(request));
                }),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };
            var proxy = GreeterPrx.FromConnection(connection);

            using var cancellationSource = new CancellationTokenSource();
            Task task = proxy.IcePingAsync(cancel: cancellationSource.Token);
            semaphore.Wait(); // Wait for the dispatch

            Assert.That(task.IsCompleted, Is.False);
            cancellationSource.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await task);

            // Now wait for the dispatch cancellation
            semaphore.Wait();

            // Verify the service still works.
            waitForCancellation = false;
            Assert.DoesNotThrowAsync(async () => await proxy.IcePingAsync());
            Assert.DoesNotThrowAsync(async () => await server.ShutdownAsync());
        }

        [TestCase(false, Protocol.Ice1)]
        [TestCase(true, Protocol.Ice1)]
        [TestCase(false, Protocol.Ice2)]
        [TestCase(true, Protocol.Ice2)]
        // Canceling the cancellation token (source) of ShutdownAsync results in a DispatchException when the operation
        // completes with an OperationCanceledException. It also test calling DisposeAsync is called instead of
        // shutdown, which call ShutdownAsync with a canceled token.
        public async Task Server_ShutdownCancelAsync(bool disposeInsteadOfShutdown, Protocol protocol)
        {
            using var semaphore = new SemaphoreSlim(0);
            await using var server = new Server
            {
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    Assert.That(cancel.CanBeCanceled, Is.True);
                    semaphore.Release();
                    await Task.Delay(-1, cancel);
                    return new OutgoingResponse(request, Payload.FromVoidReturnValue(request));
                }),
                Endpoint = TestHelper.GetUniqueColocEndpoint(protocol)
            };

            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };

            var proxy = GreeterPrx.FromConnection(connection);

            Task task = proxy.IcePingAsync();
            semaphore.Wait(); // Wait for the dispatch

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
            if (protocol == Protocol.Ice1)
            {
                Assert.ThrowsAsync<DispatchException>(async () => await task);
            }
            else
            {
                Assert.ThrowsAsync<OperationCanceledException>(async () => await task);
            }
            // Shutdown shouldn't throw.
            Assert.DoesNotThrowAsync(async () => await shutdownTask);
        }

        private class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                default;
        }
    }
}
