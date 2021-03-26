// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
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

            // A hostname cannot be used with a server endpoint
            Assert.Throws<FormatException>(
                () => new Server(new Communicator(), new ServerOptions() { Endpoints = "tcp -h foo -p 10000" }));

            // Server can only accept secure connections
            Assert.Throws<ArgumentException>(
                () => new Server(communicator,
                                 new ServerOptions()
                                 {
                                     ConnectionOptions = new()
                                     {
                                         AcceptNonSecure = NonSecure.Never
                                     },
                                     Endpoints = "tcp -h 127.0.0.1 -p 10000"
                                 }));

            // only one endpoint is allowed when a dynamic IP port (:0) is configured
            Assert.Throws<ArgumentException>(
                () => new Server(communicator,
                                 new ServerOptions()
                                 {
                                     ConnectionOptions = new()
                                     {
                                         AcceptNonSecure = NonSecure.Never
                                     },
                                     Endpoints = "ice+tcp://127.0.0.1:0?alt-endpoint=127.0.0.2:10000"
                                 }));

            // both PublishedHost and PublishedEndpoints are empty
            Assert.Throws<ArgumentException>(
                () => new Server(communicator,
                                 new ServerOptions()
                                 {
                                     PublishedEndpoints = "",
                                     PublishedHost = "",
                                     Endpoints = "ice+tcp://127.0.0.1:10000"
                                 }));

            // Accept only secure connections require tls configuration
            Assert.Throws<ArgumentException>(
                () => new Server(communicator,
                                 new ServerOptions()
                                 {
                                     ConnectionOptions = new()
                                     {
                                         AcceptNonSecure = NonSecure.Never
                                     },
                                 }));

            {
                // Activating twice the server is incorrect
                await using var server = new Server(communicator);
                server.Activate();
                Assert.Throws<InvalidOperationException>(() => server.Activate());
            }

            {
                // cannot add an dispatchInterceptor to a server after activation"
                await using var server = new Server(communicator);
                server.Activate();
                Assert.Throws<InvalidOperationException>(
                    () => server.Use(next => async (current, cancel) => await next(current, cancel)));
            }

            {
                await using var server1 = new Server(
                    communicator,
                    new ServerOptions() { Endpoints = "ice+tcp://127.0.0.1:15001" });

                Assert.ThrowsAsync<TransportException>(async () =>
                    {
                        await using var server2 = new Server(
                            communicator,
                            new ServerOptions() { Endpoints = "ice+tcp://127.0.0.1:15001" });
                    });
            }

            {
                 await using var server1 = new Server(
                    communicator,
                    new ServerOptions()
                    {
                        ColocationScope = ColocationScope.Communicator,
                        Endpoints = "ice+tcp://127.0.0.1:15001"
                    });

                IServicePrx prx = IServicePrx.Parse("ice+tcp://127.0.0.1:15001/hello", communicator);
                Connection connection = await prx.GetConnectionAsync();

                await using var server2 = new Server(communicator);
                Assert.DoesNotThrow(() => connection.Server = server2);
                Assert.DoesNotThrow(() => connection.Server = null);
                await server2.DisposeAsync();
                // Setting a deactivated server on a connection no longer raise ServerDeactivatedException
                Assert.DoesNotThrow(() => connection.Server = server2);
            }
        }

        [Test]
        public async Task Server_EndpointInformation()
        {
            await using var communicator = new Communicator();
            var server = new Server(
                communicator,
                new()
                {
                    ConnectionOptions = new()
                    {
                        AcceptNonSecure = NonSecure.Always
                    },
                    Endpoints = $"tcp -h 127.0.0.1 -p 0 -t 15000",
                    PublishedHost = "localhost"
                });

            Assert.AreEqual(1, server.Endpoints.Count);

            CollectionAssert.AreEquivalent(server.PublishedEndpoints, server.PublishedEndpoints);

            Assert.IsNotNull(server.Endpoints[0]);
            Assert.AreEqual(Transport.TCP, server.Endpoints[0].Transport);
            Assert.AreEqual("127.0.0.1", server.Endpoints[0].Host);
            Assert.IsTrue(server.Endpoints[0].Port > 0);
            Assert.AreEqual("15000", server.Endpoints[0]["timeout"]);

            Assert.IsNotNull(server.PublishedEndpoints[0]);
            Assert.AreEqual(Transport.TCP, server.PublishedEndpoints[0].Transport);
            Assert.AreEqual("localhost", server.PublishedEndpoints[0].Host);
            Assert.IsTrue(server.PublishedEndpoints[0].Port > 0);
            Assert.AreEqual("15000", server.PublishedEndpoints[0]["timeout"]);

            await server.DisposeAsync();

            await CheckServerEndpoint(communicator, "tcp -h {0} -p {1}", 10001);
            await CheckServerEndpoint(communicator, "ice+tcp://{0}:{1}", 10001);

            static async Task CheckServerEndpoint(Communicator communicator, string endpoint, int port)
            {
                await using var server = new Server(
                    communicator,
                    new()
                    {
                        Endpoints = string.Format(endpoint, "0.0.0.0", port),
                        PublishedEndpoints = string.Format(endpoint, "127.0.0.1", port)

                    });

                Assert.IsTrue(server.Endpoints.Count >= 1);
                Assert.IsTrue(server.PublishedEndpoints.Count == 1);

                foreach (Endpoint e in server.Endpoints)
                {
                    Assert.AreEqual(port, e.Port);
                }

                Assert.AreEqual("127.0.0.1", server.PublishedEndpoints[0].Host);
                Assert.AreEqual(port, server.PublishedEndpoints[0].Port);
            }
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
                InvocationTimeout = TimeSpan.FromSeconds(10),
                IsFixed = true, // ignored
                IsOneway = true,
                Path = "xxxxxxx" // ignored
            };

            await using var server = new Server(communicator,
                                                new ServerOptions() { ProxyOptions = proxyOptions });

            var proxy = server.Add("foo/bar", new ProxyTest(CheckProxy), IProxyTestPrx.Factory);
            CheckProxy(proxy);

            // change some properties
            proxy = proxy.Clone(context: new Dictionary<string, string>(), invocationTimeout: TimeSpan.FromSeconds(20));

            server.Activate();
            await proxy.SendProxyAsync(proxy); // the service executes CheckProxy on the received proxy

            IProxyTestPrx received = await proxy.ReceiveProxyAsync();

            // received inherits the proxy properties not the server options
            Assert.AreEqual(received.CacheConnection, proxy.CacheConnection);
            CollectionAssert.IsEmpty(received.Context);
            Assert.AreEqual(received.InvocationTimeout, proxy.InvocationTimeout);
            Assert.IsFalse(received.IsFixed);
            Assert.AreEqual(received.IsOneway, proxy.IsOneway);

            static void CheckProxy(IProxyTestPrx proxy)
            {
                Assert.IsFalse(proxy.CacheConnection);
                Assert.AreEqual(proxy.Context["speed"], "fast");
                Assert.AreEqual(proxy.InvocationTimeout, TimeSpan.FromSeconds(10));
                Assert.IsFalse(proxy.IsFixed);
                Assert.IsTrue(proxy.IsOneway);
                Assert.AreEqual(proxy.Path, "/foo/bar");
            }
        }

        [TestCase("tcp -h localhost -p 12345 -t 30000")]
        [TestCase("ice+tcp://localhost:12345")]
        public async Task Server_PublishedEndpoints(string endpoint)
        {
            await using var communicator = new Communicator();
            await using var server = new Server(communicator, new ServerOptions() { PublishedEndpoints = endpoint });

            Assert.AreEqual(1, server.PublishedEndpoints.Count);
            Assert.IsNotNull(server.PublishedEndpoints[0]);
            Assert.AreEqual(endpoint, server.PublishedEndpoints[0].ToString());
        }

        [TestCase(" :" )]
        [TestCase("tcp: ")]
        [TestCase(":tcp")]
        public async Task Server_InvalidEndpoints(string endpoint)
        {
            await using var communicator = new Communicator();
            Assert.Throws<FormatException>(
                () => new Server(communicator, new ServerOptions() { Endpoints = endpoint }));
        }

        private class ProxyTest : IAsyncProxyTest
        {
            private Action<IProxyTestPrx> _checkProxy;

            public ValueTask SendProxyAsync(IProxyTestPrx proxy, Current current, CancellationToken cancel)
            {
                _checkProxy(proxy);
                return default;
            }

            public ValueTask<IProxyTestPrx> ReceiveProxyAsync(Current current, CancellationToken cancel) =>
                new(IProxyTestPrx.Factory.Create(current.Server, current.Path));

            internal ProxyTest(Action<IProxyTestPrx> checkProxy) => _checkProxy = checkProxy;
        }
    }
}
