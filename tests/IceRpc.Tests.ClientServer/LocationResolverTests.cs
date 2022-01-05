// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using NUnit.Framework;

namespace IceRpc.Tests.ClientServer
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    [Timeout(30000)]
    public sealed class LocationResolverTests : IAsyncDisposable
    {
        private ConnectionPool? _pool;
        private Server? _server;

        // Note that transport loc has no special meaning with icerpc.
        [TestCase(
            "icerpc+loc://testlocation/test",
            "icerpc+loc://unknown-location/test",
            "icerpc+loc://testlocation/test?protocol=ice")]
        [TestCase("test @ adapter", "test @ unknown_adapter", "test")]
        [TestCase("test", "test @ adapter", "test2")]
        public async Task LocationResolver_ResolveAsync(string proxy, params string[] badProxies)
        {
            _pool = new ConnectionPool()
            {
                MultiplexedClientTransport = new CompositeMultiplexedClientTransport().UseSlicOverTcp(),
                SimpleClientTransport = new CompositeSimpleClientTransport().UseTcp()
            };

            var pipeline = new Pipeline();
            IProxyFormat? format = proxy.StartsWith("icerpc+", StringComparison.Ordinal) ? null : IceProxyFormat.Default;

            var indirect = GreeterPrx.Parse(proxy, pipeline, format);
            GreeterPrx direct = SetupServer(indirect.Proxy.Protocol.Code, indirect.Proxy.Path, pipeline);
            Assert.That(direct.Proxy.Endpoint, Is.Not.Null);

            if (indirect.Proxy.Endpoint is Endpoint endpoint)
            {
                pipeline.Use(LocationResolver(endpoint.Host, category: null, direct.Proxy.Endpoint!))
                        .UseBinder(_pool);
            }
            else
            {
                var identity = Identity.FromPath(indirect.Proxy.Path);
                pipeline.Use(LocationResolver(identity.Name, identity.Category, direct.Proxy.Endpoint!))
                        .UseBinder(_pool);
            }

            await indirect.SayHelloAsync("hello");
            Assert.That(indirect.Proxy.Connection, Is.Not.Null);

            foreach (string badProxy in badProxies)
            {
                var badGreeter = GreeterPrx.Parse(badProxy, pipeline, format);
                Assert.ThrowsAsync<NoEndpointException>(async () => await badGreeter.SayHelloAsync("hello"));
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

        private GreeterPrx SetupServer(ProtocolCode protocol, string path, IInvoker invoker)
        {
            string serverEndpoint = protocol == ProtocolCode.IceRpc ?
                "icerpc+tcp://127.0.0.1:0?tls=false" : "icerpc+tcp://127.0.0.1:0?protocol=ice&tls=false";
            _server = new Server
            {
                Dispatcher = new Greeter(),
                Endpoint = serverEndpoint
            };

            _server.Listen();

            // Need to create proxy after calling Listen; otherwise, the port number is still 0.
            var greeter = GreeterPrx.FromPath(path, Protocol.FromProtocolCode(protocol));
            greeter.Proxy.Endpoint = _server.Endpoint;
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
                        ((request.Endpoint is Endpoint endpoint &&
                          endpoint.Transport == "loc" &&
                          endpoint.Host == location &&
                          category == null) ||
                         (request.Endpoint == null &&
                          request.Protocol == Protocol.Ice &&
                          category != null &&
                          request.Path == new Identity(location, category).ToPath())))
                    {
                        request.Endpoint = resolvedEndpoint;
                        CollectionAssert.IsEmpty(request.AltEndpoints);
                    }
                    // else don't do anything

                    return next.InvokeAsync(request, cancel);
                });

        private class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel) => default;
        }
    }
}
