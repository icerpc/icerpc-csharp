// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    public class ServerTests
    {
        [Test]
        public async Task Server_Exceptions()
        {
            await using var communicator = new Communicator();

            {
                await using var server = new Server
                {
                    Invoker = communicator,
                    Endpoint = "tcp -h foo -p 10000"
                };

                // A DNS name cannot be used with a server endpoint
                Assert.Throws<NotSupportedException>(() => server.Listen());

                // ProxyHost can't be empty
                Assert.Throws<ArgumentException>(() => server.ProxyHost = "");

                Assert.DoesNotThrow(() => IServicePrx.FromServer(server, "/foo"));
                server.Endpoint = null;
                Assert.Throws<InvalidOperationException>(() => IServicePrx.FromServer(server, "/foo"));
            }

            {
                // Listen twice is incorrect
                await using var server = new Server
                {
                    Invoker = communicator,
                    Endpoint = TestHelper.GetUniqueColocEndpoint()
                };
                server.Listen();
                Assert.Throws<InvalidOperationException>(() => server.Listen());
            }

            {
                await using var server = new Server
                {
                    Invoker = communicator,
                    Dispatcher = new ProxyTest(),
                    Endpoint = TestHelper.GetUniqueColocEndpoint()
                };
                var proxy = IProxyTestPrx.FromServer(server, "/foo");

                Assert.ThrowsAsync<ConnectionRefusedException>(async () => await proxy.IcePingAsync());
                server.Listen();
                Assert.DoesNotThrowAsync(async () => await proxy.IcePingAsync());

                // Throws ServiceNotFoundException when Dispatcher is null
                server.Dispatcher = null;
                Assert.ThrowsAsync<ServiceNotFoundException>(async () => await proxy.IcePingAsync());
            }

            {
                // Cannot add a middleware to a router after adding a route
                var router = new Router();
                router.Map("/test", new ProxyTest());

                Assert.Throws<InvalidOperationException>(() => router.Use(next => next));
            }

            {
                await using var server1 = new Server
                {
                    Invoker = communicator,
                    Endpoint = "ice+tcp://127.0.0.1:15001"
                };
                server1.Listen();

                Assert.ThrowsAsync<TransportException>(async () =>
                    {
                        await using var server2 = new Server
                        {
                            Invoker = communicator,
                            Endpoint = "ice+tcp://127.0.0.1:15001"
                        };
                        server2.Listen();
                    });
            }

            {
                string endpoint = TestHelper.GetUniqueColocEndpoint();
                await using var server1 = new Server
                {
                    Invoker = communicator,
                    Endpoint = endpoint
                };
                server1.Listen();

                Assert.ThrowsAsync<TransportException>(async () =>
                    {
                        await using var server2 = new Server
                        {
                            Invoker = communicator,
                            Endpoint = endpoint
                        };
                        server2.Listen();
                    });
            }

            {
                await using var server1 = new Server
                {
                    Invoker = communicator,
                    Endpoint = "ice+tcp://127.0.0.1:15001"
                };

                server1.Listen();

                var prx = IServicePrx.Parse("ice+tcp://127.0.0.1:15001/hello", communicator);

                // Establishes connection:
                await prx.IcePingAsync(new Invocation { IsOneway = true });
                Connection? connection = prx.Connection;
                Assert.That(connection, Is.Not.Null);

                IDispatcher dispatcher = new ProxyTest();

                // We can set Dispatcher on an outgoing connection
                Assert.DoesNotThrow(() => connection!.Dispatcher = dispatcher);
                Assert.DoesNotThrow(() => connection!.Dispatcher = null);
            }
        }

        [TestCase("ice+tcp://127.0.0.1:0?tls=false")]
        [TestCase("tcp -h 127.0.0.1 -p 0 -t 15000")]
        public async Task Server_EndpointInformation(string endpoint)
        {
            await using var communicator = new Communicator();
            await using var server = new Server
            {
                Invoker = communicator,
                Endpoint = endpoint
            };

            Assert.AreEqual(Dns.GetHostName().ToLowerInvariant(), server.ProxyEndpoint?.Host);
            server.ProxyHost = "localhost";
            Assert.AreEqual("localhost", server.ProxyEndpoint?.Host);

            server.Listen();

            Assert.IsNotNull(server.Endpoint);
            Assert.AreEqual(Transport.TCP, server.Endpoint.Transport);
            Assert.AreEqual("127.0.0.1", server.Endpoint.Host);
            Assert.That(server.Endpoint.Port, Is.GreaterThan(0));

            if (server.Endpoint.Protocol == Protocol.Ice1)
            {
                Assert.AreEqual("15000", server.Endpoint["timeout"]);
            }

            Assert.IsNotNull(server.ProxyEndpoint);
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
            // Verifies that changing Endpoint or ProxyHost updates ProxyEndpoint.

            await using var communicator = new Communicator();
            await using var server = new Server
            {
                Invoker = communicator
            };

            Assert.IsNull(server.ProxyEndpoint);
            server.Endpoint = "ice+tcp://127.0.0.1";
            Assert.AreEqual(server.Endpoint.ToString().Replace("127.0.0.1", server.ProxyHost),
                            server.ProxyEndpoint!.ToString());
            server.ProxyHost = "foobar";
            Assert.AreEqual(server.Endpoint.ToString().Replace("127.0.0.1", server.ProxyHost),
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
        public async Task Server_InvalidEndpoints(string endpoint)
        {
            await using var communicator = new Communicator();
            Assert.Throws<FormatException>(() => new Server { Invoker = communicator, Endpoint = endpoint });
        }

        [Test]
        // When a client cancels a request, the dispatch is canceled.
        public async Task Server_RequestCancelAsync()
        {
            await using var communicator = new Communicator();
            var semaphore = new SemaphoreSlim(0);
            bool waitForCancellation = true;
            await using var server = new Server
            {
                Invoker = communicator,
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

            var proxy = IProxyTestPrx.FromServer(server, "/");

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

        [TestCase(false)]
        [TestCase(true)]
        // Canceling the cancellation token (source) of ShutdownAsync results in a DispatchException when the operation
        // completes with an OperationCanceledException. It also test calling DisposeAsync is called instead of
        //  Shutdown, which call ShutdownAsync with a canceled token.
        public async Task Server_ShutdownCancelAsync(bool disposeInsteadOfShutdown)
        {
            await using var communicator = new Communicator();
            var semaphore = new SemaphoreSlim(0);
            var server = new Server
            {
                Invoker = communicator,
                Dispatcher = new InlineDispatcher(async (request, cancel) =>
                {
                    Assert.That(cancel.CanBeCanceled, Is.True);
                    semaphore.Release();
                    await Task.Delay(-1, cancel);
                    return new OutgoingResponse(request, Payload.FromVoidReturnValue(request));
                }),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            server.Listen();

            var proxy = IProxyTestPrx.FromServer(server, "/");

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

            // Ensure the client gets a DispatchException and that shutdown doesn't throw.
            Assert.ThrowsAsync<DispatchException>(async () => await task);
            Assert.DoesNotThrowAsync(async () => await shutdownTask);
        }

        private class ProxyTest : IProxyTest
        {
            internal IProxyTestPrx? Proxy { get; set; }

            public ValueTask<IProxyTestPrx> ReceiveProxyAsync(Dispatch dispatch, CancellationToken cancel) =>
                new(dispatch.Server!.CreateEndpointlessProxy<IProxyTestPrx>(dispatch.Path));

            public ValueTask SendProxyAsync(IProxyTestPrx proxy, Dispatch dispatch, CancellationToken cancel)
            {
                Proxy = proxy;
                return default;
            }
        }
    }
}
