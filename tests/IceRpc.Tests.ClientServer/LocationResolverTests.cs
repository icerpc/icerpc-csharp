// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Features;
using IceRpc.Slice;
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

        [TestCase(
            "icerpc:/test?adapter-id=adapter",
            "icerpc:/test?adapter-id=unknown-adapter",
            "icerpc:/test")]
        [TestCase(
            "icerpc:/test",
            "icerpc:/test?adapter-id=adapter",
            "icerpc:/test2")]
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
            IProxyFormat? format = proxy.StartsWith("ice", StringComparison.Ordinal) ? null : IceProxyFormat.Default;

            var indirect = GreeterPrx.Parse(proxy, pipeline, format);
            GreeterPrx direct = SetupServer(indirect.Proxy.Protocol.Name, indirect.Proxy.Path, pipeline);
            Assert.That(direct.Proxy.Endpoint, Is.Not.Null);

            if (indirect.Proxy.Params.TryGetValue("adapter-id", out string? adapterId))
            {
                pipeline.Use(LocationResolver(adapterId, category: null, direct.Proxy.Endpoint!.Value))
                        .UseBinder(_pool);
            }
            else
            {
                var identity = Identity.FromPath(indirect.Proxy.Path);
                pipeline.Use(LocationResolver(identity.Name, identity.Category, direct.Proxy.Endpoint!.Value))
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

        private GreeterPrx SetupServer(string protocol, string path, IInvoker invoker)
        {
            string serverEndpoint = protocol == "icerpc" ?
                "icerpc://127.0.0.1:0?tls=false" : "ice://127.0.0.1:0?tls=false";
            _server = new Server
            {
                Dispatcher = new Greeter(),
                Endpoint = serverEndpoint
            };

            _server.Listen();

            // Need to create proxy after calling Listen; otherwise, the port number is still 0.
            var greeter = GreeterPrx.Parse($"{protocol}:/path");
            greeter.Proxy.Endpoint = _server.Endpoint;
            greeter.Proxy.Invoker = invoker;
            Assert.AreNotEqual(0, greeter.Proxy.Endpoint!.Value.Port);
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
                    string? adapterId =
                        request.Proxy.Params.TryGetValue("adapter-id", out string? value) ? value : null;

                    if (request.Protocol == resolvedEndpoint.Protocol &&
                        ((category == null && location == adapterId) ||
                        (category != null && adapterId == null &&
                         Identity.FromPath(request.Proxy.Path) == new Identity(location, category))))
                    {
                        var endpointSelection = new EndpointSelection()
                        {
                            Endpoint = resolvedEndpoint,
                        };
                        CollectionAssert.IsEmpty(endpointSelection.AltEndpoints);
                        request.Features = request.Features.With(endpointSelection);
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
