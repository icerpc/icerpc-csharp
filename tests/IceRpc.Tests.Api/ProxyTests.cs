// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Collections.Immutable;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(5000)]
    // [Log(LogAttributeLevel.Information)]
    public class ProxyTests
    {
        [TestCase("ice")]
        [TestCase("icerpc")]
        public async Task Proxy_ServiceAsync(string protocol)
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher, Greeter>()
                .BuildServiceProvider();

            var greeter = GreeterPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());
            var service = new ServicePrx(greeter.Proxy);

            string[] ids = new string[]
            {
                "::IceRpc::Tests::Api::Greeter",
                "::Slice::Service",
            };

            Assert.That(await service.IceIdsAsync(), Is.EqualTo(ids));
            Assert.That(await service.IceIsAAsync("::IceRpc::Tests::Api::Greeter"), Is.True);
            Assert.That(await service.IceIsAAsync("::IceRpc::Tests::Api::Foo"), Is.False);
            Assert.That(await greeter.AsAsync<GreeterPrx>(), Is.EqualTo(greeter));

            // Test that Service operation correctly forward the cancel param
            var canceled = new CancellationToken(canceled: true);
            Assert.CatchAsync<OperationCanceledException>(async () => await service.IcePingAsync(cancel: canceled));
            Assert.CatchAsync<OperationCanceledException>(async () => await service.IceIdsAsync(cancel: canceled));
            Assert.CatchAsync<OperationCanceledException>(
                async () => await service.IceIsAAsync(
                    "::IceRpc::Tests::Api::Greeter",
                    cancel: canceled));
            Assert.CatchAsync<OperationCanceledException>(
                async () => await service.AsAsync<GreeterPrx>(cancel: canceled));

            // Test that Service operations correctly forward the context
            var invocation = new Invocation
            {
                Features = new FeatureCollection().WithContext(new Dictionary<string, string> { ["foo"] = "bar" })
            };

            var pipeline = new Pipeline();
            service.Proxy.Invoker = pipeline;
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                Assert.That(request.Features.GetContext(), Is.EqualTo(invocation.Features.GetContext()));
                return next.InvokeAsync(request, cancel);
            }));

            await service.IcePingAsync(invocation);
            await service.IceIdsAsync(invocation);
            await service.IceIsAAsync("::IceRpc::Tests::Api::Greeter", invocation);
            await service.AsAsync<GreeterPrx>(invocation);
        }

        [TestCase("icerpc://localhost:10000/test?alt-endpoint=host2")]
        [TestCase("ice://localhost:10000/test")]
        [TestCase("icerpc:/test?name=value")]
        [TestCase("ice:/test#fragment")]
        [TestCase("/x/y/z")]
        [TestCase("foobar:/path#fragment")]
        [TestCase("foobar://host/path#fragment")]
        public void Proxy_GetSetProperty(string s)
        {
            var proxy = Proxy.Parse(s);

            Proxy proxy2 = proxy.Protocol == Protocol.Relative ?
                new Proxy(proxy.Protocol) { Path = proxy.Path } : new Proxy(proxy.OriginalUri!);

            // Can always read all properties
            Assert.Multiple(
                () =>
                {
                    Assert.That(proxy, Is.EqualTo(proxy2));

                    Assert.That(proxy.AltEndpoints, Is.EqualTo(proxy2.AltEndpoints));
                    Assert.That(proxy.Connection, Is.EqualTo(proxy2.Connection));
                    Assert.That(proxy.Encoding, Is.EqualTo(proxy2.Encoding));
                    Assert.That(proxy.Endpoint, Is.EqualTo(proxy2.Endpoint));
                    Assert.That(proxy.Fragment, Is.EqualTo(proxy2.Fragment));
                    Assert.That(proxy.Invoker, Is.EqualTo(proxy2.Invoker));
                    Assert.That(proxy.OriginalUri, Is.EqualTo(proxy2.OriginalUri));
                    Assert.That(proxy.Params, Is.EqualTo(proxy2.Params));
                    Assert.That(proxy.Path, Is.EqualTo(proxy2.Path));
                });

            if (proxy.OriginalUri is Uri uri)
            {
                Assert.That(proxy.ToUri(), Is.EqualTo(uri));

                if (proxy.Protocol.IsSupported)
                {
                    proxy.Endpoint = proxy.Endpoint;
                    Assert.That(proxy.OriginalUri, Is.Null);
                    Assert.That(proxy.ToUri().IsAbsoluteUri, Is.True);
                }
            }
            else
            {
                Assert.That(proxy.ToUri().IsAbsoluteUri, Is.False);
            }

            if (proxy.Protocol.IsSupported)
            {
                // Basic sets/init

                proxy2 = proxy.Endpoint is Endpoint endpoint ?
                    proxy with { Endpoint = endpoint with { Port = (ushort)(endpoint.Port + 1) } } :
                    proxy with { Path = $"{proxy.Path}/extra" };

                Assert.That(proxy, Is.Not.EqualTo(proxy2));

                proxy.AltEndpoints = ImmutableList<Endpoint>.Empty;
                Assert.That(proxy.OriginalUri, Is.Null);
                proxy.Endpoint = null;
                proxy.Connection = null;
                proxy = proxy with { Fragment = "" }; // always ok
                if (proxy.Protocol.HasFragment)
                {
                    proxy = proxy with { Fragment = "bar" };
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => proxy = proxy with { Fragment = "bar" });
                }
                proxy.Invoker = Proxy.DefaultInvoker;
                proxy.Params = ImmutableDictionary<string, string>.Empty;
                proxy = proxy with { Path = "/foo" };

                // Erroneous sets/init

                Assert.Throws<InvalidOperationException>(
                    () => proxy.AltEndpoints = ImmutableList.Create(new Endpoint(proxy.Protocol)));

                Assert.Throws<ArgumentException>(
                    () => proxy.Endpoint = Endpoint.FromString(
                        proxy.Protocol == Protocol.IceRpc ? "ice://localhost" : "icerpc://localhost"));

                proxy.Endpoint = new Endpoint(proxy.Protocol) { Host = "localhost" };

                proxy.Params = ImmutableDictionary<string, string>.Empty; // always ok
                if (proxy.Protocol != Protocol.Ice)
                {
                    Assert.Throws<InvalidOperationException>(() => proxy.Params = proxy.Params.Add("name", "value"));
                }

                proxy.Endpoint = null;

                proxy.Params = proxy.Params.Add("adapter-id", "value");

                if (proxy.Protocol == Protocol.Ice)
                {
                    Assert.Throws<ArgumentException>(() => proxy.Params = proxy.Params.SetItem("adapter-id", ""));
                }

                Assert.Throws<InvalidOperationException>(
                    () => proxy.Endpoint = new Endpoint(proxy.Protocol) { Host = "localhost" });
            }
            else
            {
                Assert.Throws<InvalidOperationException>(() => proxy.AltEndpoints = ImmutableList<Endpoint>.Empty);
                Assert.Throws<InvalidOperationException>(() => proxy.Connection = null);
                Assert.Throws<InvalidOperationException>(() => proxy.Endpoint = "icerpc://host");
                Assert.Throws<InvalidOperationException>(() => proxy = proxy with { Fragment = "bar" });
                Assert.Throws<InvalidOperationException>(() => proxy.Invoker = Proxy.DefaultInvoker);
                Assert.Throws<InvalidOperationException>(
                    () => proxy.Params = ImmutableDictionary<string, string>.Empty);

                if (proxy.Protocol != Protocol.Relative)
                {
                    Assert.Throws<InvalidOperationException>(() => proxy = proxy with { Path = "/foo" });
                }
                else
                {
                    proxy = proxy with { Path = "/foo" };
                }
            }
        }

        [Test]
        public void Proxy_SetProperty_ArgumentException()
        {
            var iceProxy = Proxy.Parse("hello:tcp -h localhost -p 10000", format: IceProxyFormat.Default);
            Assert.That(iceProxy.Protocol, Is.EqualTo(Protocol.Ice));
            var icerpcProxy = Proxy.Parse("icerpc://host.zeroc.com/hello");
            Assert.That(icerpcProxy.Protocol, Is.EqualTo(Protocol.IceRpc));

            // Endpoints protocol must match the proxy protocol
            Assert.Throws<ArgumentException>(() => iceProxy.Endpoint = icerpcProxy.Endpoint);
            Assert.Throws<ArgumentException>(() => icerpcProxy.Endpoint = iceProxy.Endpoint);
        }

        /// <summary>Test the parsing of valid proxies.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        [TestCase("ice -t:tcp -h localhost -p 10000")]
        [TestCase("icerpc:ssl -h localhost -p 10000")]
        [TestCase("identity:tcp -h 0.0.0.0")] // Any IPv4 in proxy endpoint (unusable but parses ok)
        [TestCase("identity:tcp -h \"::0\"")] // Any IPv6 address in proxy endpoint (unusable but parses ok)
        [TestCase("identity:coloc -h *")]
        [TestCase("identity -e 4.5:coloc -h *")]
        [TestCase("name -f facet:coloc -h localhost", "/name", "facet")]
        [TestCase("category/name -f facet:coloc -h localhost", "/category/name", "facet")]
        [TestCase("cat$gory/nam$ -f fac$t:coloc -h localhost", "/cat%24gory/nam%24", "fac%24t")]
        [TestCase("\\342\\x82\\254\\60\\x9\\60\\", "/%E2%82%AC0%090%5C")]
        [TestCase("bar/foo", "/bar/foo")]
        [TestCase("foo", "/foo")]
        public void Proxy_Parse_ValidInputIceFormat(string str, string? path = null, string? fragment = null)
        {
            var proxy = Proxy.Parse(str, format: IceProxyFormat.Default);

            if (path != null)
            {
                Assert.That(proxy.Path, Is.EqualTo(path));
            }

            if (fragment != null)
            {
                Assert.That(proxy.Fragment, Is.EqualTo(fragment));
            }

            Assert.That(proxy.Protocol, Is.EqualTo(Protocol.Ice));
            Assert.That(Proxy.TryParse(
                proxy.ToString(IceProxyFormat.Default),
                invoker: null,
                format: IceProxyFormat.Default,
                out Proxy? proxy2),
                Is.True);

            Assert.That(proxy, Is.EqualTo(proxy2)); // round-trip works

            // Also try with non-default ToStringMode
            proxy2 = Proxy.Parse(proxy.ToString(IceProxyFormat.ASCII), format: IceProxyFormat.Default);
            Assert.That(proxy, Is.EqualTo(proxy2));

            proxy2 = Proxy.Parse(proxy.ToString(IceProxyFormat.Compat), format: IceProxyFormat.Default);
            Assert.That(proxy, Is.EqualTo(proxy2));

            var prx = GreeterPrx.Parse(str, format: IceProxyFormat.Default);
            Assert.That(prx.Proxy.Protocol, Is.EqualTo(Protocol.Ice));
            Assert.That(GreeterPrx.TryParse(
                prx.ToString(IceProxyFormat.Default),
                invoker: null,
                format: IceProxyFormat.Default,
                out GreeterPrx prx2),
                Is.True);
            Assert.That(prx, Is.EqualTo(prx2)); // round-trip works
        }

        [Test]
        public async Task Proxy_ReceiveProxyAsync()
        {
            var service = new ProxyTest();

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ => service)
                .BuildServiceProvider();

            var prx = ProxyTestPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());

            ProxyTestPrx? received = await prx.ReceiveProxyAsync();
            Assert.That(received, Is.Null);

            // Check that the received proxy "inherits" the invoker of the caller.
            service.Prx = ProxyTestPrx.FromPath("/foo");
            received = await prx.ReceiveProxyAsync();
            Assert.That(received?.Proxy.Invoker, Is.EqualTo(Proxy.DefaultInvoker));

            var pipeline = new Pipeline();
            prx.Proxy.Invoker = pipeline;
            received = await prx.ReceiveProxyAsync();
            Assert.That(received?.Proxy.Invoker, Is.EqualTo(pipeline));

            // Same with an endpoint
            service.Prx = ProxyTestPrx.Parse("icerpc://localhost/foo");
            received = await prx.ReceiveProxyAsync();
            Assert.That(received?.Proxy.Endpoint, Is.EqualTo(service.Prx?.Proxy.Endpoint));
            Assert.That(received?.Proxy.Invoker, Is.EqualTo(pipeline));
        }

        [Test]
        public async Task Proxy_SendProxyAsync()
        {
            var service = new ProxyTest();

            // First verify that the invoker of a proxy received over an incoming request is by default the default
            // invoker.
            await using ServiceProvider serviceProvider1 = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ => service)
                .BuildServiceProvider();

            var prx = ProxyTestPrx.FromConnection(serviceProvider1.GetRequiredService<Connection>());
            await prx.SendProxyAsync(prx);
            Assert.That(service.Prx, Is.Not.Null);
            Assert.That(service.Prx?.Proxy.Invoker, Is.EqualTo(Proxy.DefaultInvoker));

            // Now with a router and the ProxyInvoker middleware - we set the invoker on the proxy received by the
            // service.
            var pipeline = new Pipeline();

            await using ServiceProvider serviceProvider2 = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Map<IProxyTest>(service);
                    router.UseFeature(new DecodePayloadOptions { ProxyInvoker = pipeline });
                    return router;
                })
                .BuildServiceProvider();
            prx = ProxyTestPrx.FromConnection(serviceProvider2.GetRequiredService<Connection>());

            service.Prx = null;
            await prx.SendProxyAsync(prx);
            Assert.That(service.Prx, Is.Not.Null);
            Assert.That(service.Prx?.Proxy.Invoker, Is.EqualTo(pipeline));
        }

        [Test]
        public void Proxy_UriOptions()
        {
            string proxyString = "icerpc://localhost:10000/test";

            var proxy = Proxy.Parse(proxyString);

            Assert.That(proxy.Path, Is.EqualTo("/test"));

            string complicated = $"{proxyString}?encoding=1.1&alt-endpoint=localhost";
            proxy = Proxy.Parse(complicated);

            Assert.That(proxy.Encoding, Is.EqualTo(Encoding.Slice11));
            Endpoint altEndpoint = proxy.AltEndpoints[0];
            Assert.That(proxy.AltEndpoints.Count, Is.EqualTo(1));
        }

        [TestCase("1.3")]
        [TestCase("2.1")]
        public async Task Proxy_NotSupportedEncoding(string encoding)
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher, Greeter>()
                .BuildServiceProvider();

            var service = ServicePrx.FromConnection(
                serviceProvider.GetRequiredService<Connection>(),
                GreeterPrx.DefaultPath);
            service.Proxy.Encoding = Encoding.FromString(encoding);
            await service.IcePingAsync(); // works fine, we use the protocol's encoding in this case
        }

        [Test]
        public async Task Proxy_FactoryMethodsAsync()
        {
            Assert.That(ServicePrx.DefaultPath, Is.EqualTo("/Slice.Service"));

            var proxy = Proxy.FromPath("/test");
            Assert.That(proxy.Path, Is.EqualTo("/test"));
            Assert.That(proxy.Endpoint, Is.Null);

            Assert.That(GreeterPrx.DefaultPath, Is.EqualTo("/IceRpc.Tests.Api.Greeter"));

            var greeter = GreeterPrx.FromPath("/test");
            Assert.That(greeter.Proxy.Path, Is.EqualTo("/test"));
            Assert.That(greeter.Proxy.Endpoint, Is.Null);

            dynamic? capture = null;

            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Use(next => new InlineDispatcher((request, cancel) =>
                    {
                        capture = new
                        {
                            ServerConnection = request.Connection,
                            Service = ServicePrx.FromConnection(request.Connection),
                            Greeter = GreeterPrx.FromConnection(request.Connection)
                        };
                        return new(new OutgoingResponse(request));
                    }));
                    return router;
                })
                .BuildServiceProvider();

            Connection connection = serviceProvider.GetRequiredService<Connection>();
            proxy = Proxy.FromConnection(connection, ServicePrx.DefaultPath);
            Assert.That(proxy.Path, Is.EqualTo(ServicePrx.DefaultPath));
            Assert.That(proxy.Connection, Is.EqualTo(connection));
            Assert.That(proxy.Endpoint, Is.EqualTo(connection.RemoteEndpoint));

            greeter = GreeterPrx.FromConnection(connection);
            Assert.That(greeter.Proxy.Path, Is.EqualTo(GreeterPrx.DefaultPath));
            Assert.That(greeter.Proxy.Connection, Is.EqualTo(connection));
            Assert.That(greeter.Proxy.Endpoint, Is.EqualTo(connection.RemoteEndpoint));

            await ServicePrx.FromConnection(connection).IcePingAsync();

            Assert.That(capture, Is.Not.Null);
            Assert.That(capture!.Service.Proxy.Path, Is.EqualTo(ServicePrx.DefaultPath));
            Assert.That(capture.Service.Proxy.Connection, Is.EqualTo(capture.ServerConnection));
            Assert.That(capture.Service.Proxy.Endpoint, Is.Null);

            Assert.That(capture.Greeter.Proxy.Path, Is.EqualTo(GreeterPrx.DefaultPath));
            Assert.That(capture.Greeter.Proxy.Connection, Is.EqualTo(capture.ServerConnection));

            Assert.That(capture.Greeter.Proxy.Endpoint, Is.Null);
        }

        public class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(string message, Dispatch dispatch, CancellationToken cancel) => default;
        }

        private class ProxyTest : Service, IProxyTest
        {
            internal ProxyTestPrx? Prx { get; set; }

            public ValueTask<ProxyTestPrx?> ReceiveProxyAsync(Dispatch dispatch, CancellationToken cancel) => new(Prx);

            public ValueTask SendProxyAsync(ProxyTestPrx prx, Dispatch dispatch, CancellationToken cancel)
            {
                Prx = prx;
                return default;
            }
        }
    }
}
