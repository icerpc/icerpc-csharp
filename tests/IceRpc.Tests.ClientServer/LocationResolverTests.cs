// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public sealed class LocationResolverTests : IAsyncDisposable
    {
        private ConnectionPool? _pool;
        private Server? _server;

        [TestCase("ice+loc://testlocation/test", "ice+loc://unknown-location/test", "test", "test @ testlocation")]
        [TestCase("test @ adapter", "test @ unknown_adapter", "test", "ice+loc://adapter/test")]
        [TestCase("test", "test @ adapter", "test2", "ice+loc://adapter/test")]
        public async Task LocationResolver_ResolveAsync(string proxy, params string[] badProxies)
        {
            _pool = new ConnectionPool();
            var pipeline = new Pipeline();

            var indirect = GreeterPrx.Parse(proxy, pipeline);
            GreeterPrx direct = SetupServer(indirect.Proxy.Protocol, indirect.Proxy.Path, pipeline);
            Assert.That(direct.Proxy.Endpoint, Is.Not.Null);

            if (indirect.Proxy.Endpoint is Endpoint locEndpoint)
            {
                pipeline.Use(LocationResolver(indirect.Proxy.Endpoint.Host, category: null, direct.Proxy.Endpoint!),
                             Interceptors.Binder(_pool));
            }
            else
            {
                var identity = indirect.Proxy.GetIdentity();
                pipeline.Use(LocationResolver(identity.Name, identity.Category, direct.Proxy.Endpoint!),
                                  Interceptors.Binder(_pool));
            }

            await indirect.SayHelloAsync();
            Assert.That(indirect.Proxy.Connection, Is.Not.Null);

            foreach (string badProxy in badProxies)
            {
                var badGreeter = GreeterPrx.Parse(badProxy, pipeline);
                Assert.ThrowsAsync<NoEndpointException>(async () => await badGreeter.SayHelloAsync());
            }
        }

        [TearDown]
        public async ValueTask DisposeAsync()
        {
            if (_server != null)
            {
                await _server.DisposeAsync();
            }
            if (_pool != null)
            {
                await _pool.DisposeAsync();
            }
        }

        private GreeterPrx SetupServer(Protocol protocol, string path, IInvoker invoker)
        {
            _server = new Server
            {
                Dispatcher = new Greeter(),
                Endpoint = protocol == Protocol.Ice2 ? "ice+tcp://127.0.0.1:0?tls=false" : "tcp -h 127.0.0.1 -p 0",
                // TODO use localhost see https://github.com/dotnet/runtime/issues/53447
                HostName = "127.0.0.1"
            };

            _server.Listen();

            // Need to create proxy after calling Listen; otherwise, the port number is still 0.
            var greeter = GreeterPrx.FromServer(_server, path);
            greeter.Proxy.Invoker = invoker;
            Assert.AreNotEqual(0, greeter.Proxy.Endpoint!.Port);
            return greeter;
        }

        // A very simple location resolver interceptor with no cache that resolves a single location represented by
        // location and category.
        private static Func<IInvoker, IInvoker> LocationResolver(
            string location,
            string? category,
            Endpoint resolvedEndpoint) =>
            next => new InlineInvoker(
                (request, cancel) =>
                {
                    if ((request.Protocol == resolvedEndpoint.Protocol) &&
                        ((request.Endpoint is Endpoint locEndpoint &&
                          locEndpoint.Transport == Transport.Loc &&
                          locEndpoint.Host == location &&
                           category == null) ||
                         (request.Endpoint == null &&
                          request.Protocol == Protocol.Ice1 &&
                          category != null &&
                          request.GetIdentity() == new Identity(location, category))))
                    {
                        request.Endpoint = resolvedEndpoint;
                        CollectionAssert.IsEmpty(request.AltEndpoints);
                    }
                    // else don't do anything

                    return next.InvokeAsync(request, cancel);
                });

        private class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) => default;
        }
    }
}
