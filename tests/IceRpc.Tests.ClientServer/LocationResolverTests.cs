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
            _pool = new ConnectionPool(new ConnectionOptions
            {
                MultiplexedClientTransport = new CompositeMultiplexedClientTransport().UseSlicOverTcp(),
                SimpleClientTransport = new CompositeSimpleClientTransport().UseTcp()
            });

            var pipeline = new Pipeline();
            IProxyFormat? format = proxy.StartsWith("ice", StringComparison.Ordinal) ? null : IceProxyFormat.Default;

            var indirect = GreeterPrx.Parse(proxy, pipeline, format);
            GreeterPrx direct = SetupServer(indirect.Proxy.Protocol.Name, indirect.Proxy.Path, pipeline);
            Assert.That(direct.Proxy.Endpoint, Is.Not.Null);

            if (indirect.Proxy.Params.TryGetValue("adapter-id", out string? adapterId))
            {
                pipeline.Use(LocationResolver(isAdapterId: true, adapterId, direct.Proxy.Endpoint!.Value))
                        .UseBinder(_pool);
            }
            else
            {
                pipeline.Use(LocationResolver(isAdapterId: false, indirect.Proxy.Path, direct.Proxy.Endpoint!.Value))
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
            string serverEndpoint = $"{protocol}://127.0.0.1:0";
            _server = new Server(new Greeter(), serverEndpoint);
            _server.Listen();

            // Need to create proxy after calling Listen; otherwise, the port number is still 0.
            var greeter = GreeterPrx.Parse($"{protocol}:/path");
            greeter.Proxy.Endpoint = _server.Endpoint;
            greeter.Proxy.Invoker = invoker;
            Assert.That(greeter.Proxy.Endpoint!.Value.Port, Is.Not.EqualTo(0));
            return greeter;
        }

        // A very simple location resolver interceptor with no cache that resolves a single location represented by
        // location and category.
        private static Func<IInvoker, IInvoker> LocationResolver(
            bool isAdapterId,
            string locationValue,
            Endpoint resolvedEndpoint) =>
            next => new InlineInvoker(
                (request, cancel) =>
                {
                    string? adapterId =
                        request.Proxy.Params.TryGetValue("adapter-id", out string? value) ? value : null;

                    if (request.Protocol == resolvedEndpoint.Protocol &&
                        ((isAdapterId && adapterId == locationValue) ||
                        (!isAdapterId && adapterId == null && request.Proxy.Path == locationValue)))
                    {
                        var endpointSelection = new EndpointSelection()
                        {
                            Endpoint = resolvedEndpoint,
                        };
                        Assert.That(endpointSelection.AltEndpoints, Is.Empty);
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
