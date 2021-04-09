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

            {
                await using var server = new Server
                {
                    Communicator = communicator,
                    Endpoint = "tcp -h foo -p 10000"
                };

                // A DNS name cannot be used with a server endpoint
                Assert.Throws<NotSupportedException>(() => _ = server.ListenAndServeAsync());
            }

            {
                // Async twice is incorrect
                await using var server = new Server { Communicator = communicator };
                _ = server.ListenAndServeAsync();
                Assert.Throws<InvalidOperationException>(() => _ = server.ListenAndServeAsync());
            }

            {
                // cannot add a middleware to a router after adding a route
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
                _ = server1.ListenAndServeAsync();

                Assert.ThrowsAsync<TransportException>(async () =>
                    {
                        await using var server2 = new Server
                        {
                            Communicator = communicator,
                            Endpoint = "ice+tcp://127.0.0.1:15001"
                        };
                        _ = server2.ListenAndServeAsync();

                    });
            }

            {
                await using var server1 = new Server
                {
                    Communicator = communicator,
                    Endpoint = "ice+tcp://127.0.0.1:15001"
                };

                _ = server1.ListenAndServeAsync();

                var prx = IServicePrx.Parse("ice+tcp://127.0.0.1:15001/hello", communicator);
                Connection connection = await prx.GetConnectionAsync();

                await using var server2 = new Server { Communicator = communicator };
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
            await using var server = new Server
            {
                Communicator = communicator,
                ConnectionOptions = new()
                {
                    AcceptNonSecure = NonSecure.Always
                },
                Endpoint = $"tcp -h 127.0.0.1 -p 0 -t 15000",
                ProxyHost = "localhost"
            };

            _ = server.ListenAndServeAsync();

            Endpoint serverEndpoint = Endpoint.Parse(server.Endpoint);
            Endpoint proxyEndpoint = Endpoint.Parse(server.ProxyEndpoint);

            Assert.AreEqual(Transport.TCP, serverEndpoint.Transport);
            Assert.AreEqual("127.0.0.1", serverEndpoint.Host);
            Assert.IsTrue(serverEndpoint.Port > 0);
            Assert.AreEqual("15000", serverEndpoint["timeout"]);

            Assert.AreEqual(Transport.TCP, proxyEndpoint.Transport);
            Assert.AreEqual("localhost", proxyEndpoint.Host);
            Assert.AreEqual(serverEndpoint.Port, proxyEndpoint.Port);
            Assert.AreEqual("15000", proxyEndpoint["timeout"]);
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

            await using var server = new Server
            {
                Communicator = communicator,
                ProxyOptions = proxyOptions
            };

            var service = new ProxyTest();
            IProxyTestPrx? proxy = server.Add("/foo/bar", service, IProxyTestPrx.Factory);
            CheckProxy(proxy);

            // change some properties
            proxy.Context = new Dictionary<string, string>();
            proxy.InvocationTimeout = TimeSpan.FromSeconds(20);

            _ = server.ListenAndServeAsync();
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

        [TestCase(" :" )]
        [TestCase("tcp: ")]
        [TestCase(":tcp")]
        public async Task Server_InvalidEndpoints(string endpoint)
        {
            await using var communicator = new Communicator();
            Assert.Throws<FormatException>(() => new Server { Communicator = communicator, Endpoint = endpoint });
        }

        private class ProxyTest : IAsyncProxyTest
        {
            internal IProxyTestPrx? Proxy { get; set; }

            public ValueTask SendProxyAsync(IProxyTestPrx proxy, Current current, CancellationToken cancel)
            {
                Proxy = proxy;
                return default;
            }

            public ValueTask<IProxyTestPrx> ReceiveProxyAsync(Current current, CancellationToken cancel) =>
                new(IProxyTestPrx.Factory.Create(current.Server, current.Path));
        }
    }
}
