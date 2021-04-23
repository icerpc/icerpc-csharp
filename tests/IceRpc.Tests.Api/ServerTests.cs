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
                    Communicator = communicator,
                    Endpoint = "tcp -h foo -p 10000"
                };

                // A DNS name cannot be used with a server endpoint
                Assert.Throws<NotSupportedException>(() => server.Listen());

                // ProxyHost can't be empty
                Assert.Throws<ArgumentException>(() => server.ProxyHost = "");

                Assert.DoesNotThrow(() => server.CreateProxy<IServicePrx>("/foo"));
                server.Endpoint = "";
                Assert.Throws<InvalidOperationException>(() => server.CreateProxy<IServicePrx>("/foo"));
            }

            {
                // Listen twice is incorrect
                await using var server = new Server
                {
                    Communicator = communicator,
                    Endpoint = TestHelper.GetUniqueColocEndpoint()
                };
                server.Listen();
                Assert.Throws<InvalidOperationException>(() => server.Listen());
            }

            {
                await using var server = new Server
                {
                    Communicator = communicator,
                    Dispatcher = new ProxyTest(),
                    Endpoint = TestHelper.GetUniqueColocEndpoint()
                };
                var proxy = server.CreateProxy<IProxyTestPrx>("/foo");

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
                    Communicator = communicator,
                    Endpoint = "ice+tcp://127.0.0.1:15001"
                };
                server1.Listen();

                Assert.ThrowsAsync<TransportException>(async () =>
                    {
                        await using var server2 = new Server
                        {
                            Communicator = communicator,
                            Endpoint = "ice+tcp://127.0.0.1:15001"
                        };
                        server2.Listen();
                    });
            }

            {
                string endpoint = TestHelper.GetUniqueColocEndpoint();
                await using var server1 = new Server
                {
                    Communicator = communicator,
                    Endpoint = endpoint
                };
                server1.Listen();

                Assert.ThrowsAsync<TransportException>(async () =>
                    {
                        await using var server2 = new Server
                        {
                            Communicator = communicator,
                            Endpoint = endpoint
                        };
                        server2.Listen();
                    });
            }

            {
                await using var server1 = new Server
                {
                    Communicator = communicator,
                    Endpoint = "ice+tcp://127.0.0.1:15001"
                };

                server1.Listen();

                var prx = IServicePrx.Parse("ice+tcp://127.0.0.1:15001/hello", communicator);
                Connection connection = await prx.GetConnectionAsync();

                await using var server2 = new Server
                {
                    Communicator = communicator,
                    Endpoint = TestHelper.GetUniqueColocEndpoint()
                };

                Assert.DoesNotThrow(() => connection.Server = server2);
                Assert.DoesNotThrow(() => connection.Server = null);
                await server2.DisposeAsync();
                // Setting a deactivated server on a connection no longer raise ServerDeactivatedException
                Assert.DoesNotThrow(() => connection.Server = server2);
            }
        }

        [TestCase("ice+tcp://127.0.0.1:0")]
        [TestCase("tcp -h 127.0.0.1 -p 0 -t 15000")]
        public async Task Server_EndpointInformation(string endpoint)
        {
            await using var communicator = new Communicator();
            await using var server = new Server
            {
                Communicator = communicator,
                ConnectionOptions = new()
                {
                    AcceptNonSecure = NonSecure.Always
                },
                Endpoint = endpoint
            };

            Assert.AreEqual(Dns.GetHostName().ToLowerInvariant(), Endpoint.Parse(server.ProxyEndpoint).Host);
            server.ProxyHost = "localhost";
            Assert.AreEqual("localhost", Endpoint.Parse(server.ProxyEndpoint).Host);

            server.Listen();

            var serverEndpoint = Endpoint.Parse(server.Endpoint);
            var proxyEndpoint = Endpoint.Parse(server.ProxyEndpoint);

            Assert.AreEqual(Transport.TCP, serverEndpoint.Transport);
            Assert.AreEqual("127.0.0.1", serverEndpoint.Host);
            Assert.That(serverEndpoint.Port, Is.GreaterThan(0));

            if (serverEndpoint.Protocol == Protocol.Ice1)
            {
                Assert.AreEqual("15000", serverEndpoint["timeout"]);
            }

            Assert.AreEqual(Transport.TCP, proxyEndpoint.Transport);
            Assert.AreEqual("localhost", proxyEndpoint.Host);
            Assert.AreEqual(serverEndpoint.Port, proxyEndpoint.Port);

            if (proxyEndpoint.Protocol == Protocol.Ice1)
            {
                Assert.AreEqual("15000", proxyEndpoint["timeout"]);
            }
        }

        [Test]
        public async Task Server_EndpointAsync()
        {
            // Verifies that changing Endpoint or ProxyHost updates ProxyEndpoint.

            await using var communicator = new Communicator();
            await using var server = new Server
            {
                Communicator = communicator
            };

            Assert.IsEmpty(server.ProxyEndpoint);
            server.Endpoint = "ice+tcp://127.0.0.1";
            Assert.AreEqual(server.Endpoint.Replace("127.0.0.1", server.ProxyHost), server.ProxyEndpoint);
            server.ProxyHost = "foobar";
            Assert.AreEqual(server.Endpoint.Replace("127.0.0.1", server.ProxyHost), server.ProxyEndpoint);

            // Verifies that changing Endpoint updates Protocol
            Assert.AreEqual(Protocol.Ice2, server.Protocol);
            server.Endpoint = "tcp -h 127.0.0.1 -p 0";
            Assert.AreEqual(Protocol.Ice1, server.Protocol);
            server.Endpoint = "";
            Assert.AreEqual(Protocol.Ice2, server.Protocol);
        }

        [Test]
        public async Task Server_ProxyOptionsAsync()
        {
            await using var communicator = new Communicator();

            var proxyOptions = new ProxyOptions()
            {
                CacheConnection = false,
                // no need to set Communicator
                Context = new Dictionary<string, string>() { ["speed"] = "fast" },
                InvocationTimeout = TimeSpan.FromSeconds(10)
            };

            var service = new ProxyTest();

            await using var server = new Server
            {
                Communicator = communicator,
                ProxyOptions = proxyOptions,
                Dispatcher = service,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            IProxyTestPrx? proxy = server.CreateProxy<IProxyTestPrx>("/foo/bar");
            CheckProxy(proxy);

            // change some properties
            proxy.Context = new Dictionary<string, string>();
            proxy.InvocationTimeout = TimeSpan.FromSeconds(20);

            server.Listen();
            await proxy.SendProxyAsync(proxy);
            // The server always unmarshals the proxy as a fixed proxy
            Assert.IsNotNull(service.Proxy);
            CheckProxy(service.Proxy!);

            IProxyTestPrx received = await proxy.ReceiveProxyAsync();

            // received inherits the proxy properties not the server options
            Assert.AreEqual(received.CacheConnection, proxy.CacheConnection);
            CollectionAssert.IsEmpty(received.Context);
            Assert.AreEqual(received.InvocationTimeout, proxy.InvocationTimeout);
            Assert.AreEqual(received.IsOneway, proxy.IsOneway);

            static void CheckProxy(IProxyTestPrx proxy)
            {
                Assert.IsFalse(proxy.CacheConnection);
                Assert.AreEqual("fast", proxy.Context["speed"]);
                Assert.AreEqual(TimeSpan.FromSeconds(10), proxy.InvocationTimeout);
                Assert.AreEqual("/foo/bar", proxy.Path);
            }
        }

        [TestCase(" :")]
        [TestCase("tcp: ")]
        [TestCase(":tcp")]
        public async Task Server_InvalidEndpoints(string endpoint)
        {
            await using var communicator = new Communicator();
            Assert.Throws<FormatException>(() => new Server { Communicator = communicator, Endpoint = endpoint });
        }

        [Test]
        // When a client cancels a request, the dispatch is canceled.
        public async Task Server_RequestCancelAsync()
        {
            await using var communicator = new Communicator();
            var service = new ProxyTest();

            await using var server = new Server
            {
                Communicator = communicator,
                Dispatcher = service,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            server.Listen();

            var proxy = server.CreateProxy<IProxyTestPrx>("/");

            using var cancellationSource = new CancellationTokenSource();
            Task task = proxy.WaitForCancelAsync(cancel: cancellationSource.Token);
            await service.WaitForCancelInProgress;
            Assert.IsFalse(task.IsCompleted);
            cancellationSource.Cancel();
            Assert.CatchAsync<OperationCanceledException>(async () => await task);

            // Verify service still works
            Assert.DoesNotThrowAsync(async () => await proxy.IcePingAsync());
            Assert.DoesNotThrowAsync(async () => await server.ShutdownAsync());
        }

        [Test]
        // Canceling the cancellation token (source) of ShutdownAsync results in a ServerException when the operation
        // completes with an OperationCanceledException.
        public async Task Server_ShutdownCancelAsync()
        {
            await using var communicator = new Communicator();
            var service = new ProxyTest();

            await using var server = new Server
            {
                Communicator = communicator,
                Dispatcher = service,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            server.Listen();

            var proxy = server.CreateProxy<IProxyTestPrx>("/");

            Task task = proxy.WaitForCancelAsync();
            await service.WaitForCancelInProgress;

            using var cancellationSource = new CancellationTokenSource();
            Task shutdownTask = server.ShutdownAsync(cancellationSource.Token);
            Assert.IsFalse(task.IsCompleted);
            Assert.IsFalse(shutdownTask.IsCompleted);

            cancellationSource.Cancel();
            Assert.ThrowsAsync<ServerException>(async () => await task);
            Assert.DoesNotThrowAsync(async () => await shutdownTask);
        }

        [Test]
        // Like Server_ShutdownCancelAsync, except ShutdownAsync with a canceled token is called by DisposeAsync.
        public async Task Server_DisposeAsync()
        {
            await using var communicator = new Communicator();
            var service = new ProxyTest();

            var server = new Server
            {
                Communicator = communicator,
                Dispatcher = service,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };

            server.Listen();

            var proxy = server.CreateProxy<IProxyTestPrx>("/");

            Task task = proxy.WaitForCancelAsync();
            await service.WaitForCancelInProgress;
            Assert.IsFalse(task.IsCompleted);
            ValueTask disposeTask = server.DisposeAsync();
            Assert.ThrowsAsync<ServerException>(async () => await task);
            Assert.DoesNotThrowAsync(async () => await disposeTask);
        }

        private class ProxyTest : IAsyncProxyTest
        {
            internal IProxyTestPrx? Proxy { get; set; }

            internal Task WaitForCancelInProgress => _waitForCancelInProgressSource.Task;

            private TaskCompletionSource<object?> _waitForCancelInProgressSource =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask<IProxyTestPrx> ReceiveProxyAsync(Current current, CancellationToken cancel) =>
                new(current.Server.CreateRelativeProxy<IProxyTestPrx>(current.Path));

            public ValueTask SendProxyAsync(IProxyTestPrx proxy, Current current, CancellationToken cancel)
            {
                Proxy = proxy;
                return default;
            }

            public async ValueTask WaitForCancelAsync(Current current, CancellationToken cancel)
            {
                Assert.IsTrue(cancel.CanBeCanceled);
                _waitForCancelInProgressSource.SetResult(null);
                while (!cancel.IsCancellationRequested)
                {
                    await Task.Yield();
                }
                cancel.ThrowIfCancellationRequested(); // to make it typical
            }
        }
    }
}
