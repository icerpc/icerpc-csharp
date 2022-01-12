// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

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

            var prx = GreeterPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());
            await prx.IcePingAsync();

            string[] ids = new string[]
            {
                "::IceRpc::Service",
                "::IceRpc::Tests::Api::Greeter",
            };
            CollectionAssert.AreEqual(ids, await prx.IceIdsAsync());

            Assert.That(await prx.IceIsAAsync("::IceRpc::Tests::Api::Greeter"), Is.True);
            Assert.That(await prx.IceIsAAsync("::IceRpc::Tests::Api::Foo"), Is.False);

            Assert.AreEqual(prx, await prx.AsAsync<GreeterPrx>());

            // Test that Service operation correctly forward the cancel param
            var canceled = new CancellationToken(canceled: true);
            Assert.ThrowsAsync<OperationCanceledException>(async () => await prx.IcePingAsync(cancel: canceled));
            Assert.ThrowsAsync<OperationCanceledException>(async () => await prx.IceIdsAsync(cancel: canceled));
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await prx.IceIsAAsync("::IceRpc::Tests::Api::Greeter", cancel: canceled));
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await prx.AsAsync<GreeterPrx>(cancel: canceled));

            // Test that Service operations correctly forward the context
            var invocation = new Invocation
            {
                Context = new Dictionary<string, string> { ["foo"] = "bar" }
            };

            var pipeline = new Pipeline();
            prx.Proxy.Invoker = pipeline;
            pipeline.Use(next => new InlineInvoker((request, cancel) =>
            {
                Assert.AreEqual(request.Features.GetContext(), invocation.Context);
                return next.InvokeAsync(request, cancel);
            }));

            await prx.IcePingAsync(invocation);
            await prx.IceIdsAsync(invocation);
            await prx.IceIsAAsync("::IceRpc::Tests::Api::Greeter", invocation);
            await prx.AsAsync<GreeterPrx>(invocation);
        }

        [TestCase("icerpc://localhost:10000/test")]
        [TestCase("test:tcp -h localhost -p 10000")]
        public void Proxy_SetProperty(string s)
        {
            IProxyFormat? format = s.StartsWith("icerpc", StringComparison.Ordinal) ? null : IceProxyFormat.Default;

            var proxy = Proxy.Parse(s, format: format);

            proxy.Encoding = Encoding.Slice11;
            Assert.AreEqual(Encoding.Slice11, proxy.Encoding);
            proxy.Encoding = Encoding.Slice20;
            Assert.AreEqual(Encoding.Slice20, proxy.Encoding);

            var proxy2 = proxy.Clone();
            proxy2.Endpoint = proxy2.Endpoint! with { Port = (ushort)(proxy.Endpoint!.Port + 1) };

            Assert.AreNotEqual(proxy.Endpoint, proxy2.Endpoint);
            proxy.Endpoint = proxy2.Endpoint;
            Assert.AreEqual(proxy.Endpoint, proxy2.Endpoint);
        }

        [Test]
        public void Proxy_SetProperty_ArgumentException()
        {
            var iceProxy = Proxy.Parse("hello:tcp -h localhost -p 10000", format: IceProxyFormat.Default);
            Assert.AreEqual(Protocol.Ice, iceProxy.Protocol);
            var icerpcProxy = Proxy.Parse("icerpc://host.zeroc.com/hello");
            Assert.AreEqual(Protocol.IceRpc, icerpcProxy.Protocol);

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
        public void Proxy_Parse_ValidInputIceFormat(string str, string? path = null, string? fragment = null)
        {
            var proxy = Proxy.Parse(str, format: IceProxyFormat.Default);

            if (path != null)
            {
                Assert.AreEqual(path, proxy.Path);
            }

            if (fragment != null)
            {
                Assert.That(proxy.Fragment, Is.EqualTo(fragment));
            }

            Assert.AreEqual(Protocol.Ice, proxy.Protocol);
            Assert.That(Proxy.TryParse(
                proxy.ToString(IceProxyFormat.Default),
                invoker: null,
                format: IceProxyFormat.Default,
                out Proxy? proxy2),
                Is.True);
            Assert.AreEqual(proxy, proxy2); // round-trip works

            // Also try with non-default ToStringMode
            proxy2 = Proxy.Parse(proxy.ToString(IceProxyFormat.ASCII), format: IceProxyFormat.Default);
            Assert.AreEqual(proxy, proxy2);

            proxy2 = Proxy.Parse(proxy.ToString(IceProxyFormat.Compat), format: IceProxyFormat.Default);
            Assert.AreEqual(proxy, proxy2);

            var prx = GreeterPrx.Parse(str, format: IceProxyFormat.Default);
            Assert.AreEqual(Protocol.Ice, prx.Proxy.Protocol);
            Assert.That(GreeterPrx.TryParse(
                prx.ToString(IceProxyFormat.Default),
                invoker: null,
                format: IceProxyFormat.Default,
                out GreeterPrx prx2),
                Is.True);
            Assert.AreEqual(prx, prx2); // round-trip works

            var identity = Identity.FromPath(prx.Proxy.Path);
            var identity2 = Identity.FromPath(prx2.Proxy.Path);
            Assert.AreEqual(identity, identity2);
            Assert.AreEqual(prx.Proxy.Fragment, prx2.Proxy.Fragment); // facets
        }

        [TestCase("icerpc://host.zeroc.com/path?encoding=foo")]
        [TestCase("icerpc://host.zeroc.com/identity#facet", "/identity", "facet")]
        [TestCase("icerpc://host.zeroc.com/identity#facet#?!$x", "/identity", "facet#?!$x")]
        [TestCase("icerpc://host.zeroc.com/identity#", "/identity", "")]
        [TestCase("icerpc://host.zeroc.com/identity##%23f", "/identity", "#%23f")]
        [TestCase("ice://host.zeroc.com/identity?tls=false")]
        [TestCase("ice://host.zeroc.com/identity?tls=true")] // TODO: add no tls test
        [TestCase("icerpc://host.zeroc.com:1000/category/name")]
        [TestCase("icerpc://host.zeroc.com:1000/loc0/loc1/category/name")]
        [TestCase("icerpc://host.zeroc.com/category/name%20with%20space", "/category/name%20with%20space")]
        [TestCase("icerpc://host.zeroc.com/category/name with space", "/category/name%20with%20space")]
        [TestCase("icerpc://host.zeroc.com//identity")]
        [TestCase("icerpc://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com")]
        [TestCase("icerpc://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com:10000")]
        [TestCase("icerpc://[::1]:10000/identity?alt-endpoint=host1:10000,host2,host3,host4")]
        [TestCase("icerpc://[::1]:10000/identity?alt-endpoint=host1:10000&alt-endpoint=host2,host3&alt-endpoint=[::2]")]
        [TestCase("icerpc:location//identity#facet", "/location//identity")]
        [TestCase("icerpc://host.zeroc.com//identity")]
        [TestCase("icerpc://host.zeroc.com/\x7f€$%/!#$'()*+,:;=@[] %2F", "/%7F%E2%82%AC$%25/!", "$'()*+,:;=@[]%20%2F")]
        [TestCase("icerpc://host.zeroc.com/identity#\x7f€$%/!#$'()*+,:;=@[] %2F", "/identity", "%7F%E2%82%AC$%25/!#$'()*+,:;=@[]%20%2F")]
        [TestCase(@"icerpc://host.zeroc.com/foo\bar\n\t!", "/foo/bar/n/t!")] // Parser converts \ to /
        // another syntax for empty port
        [TestCase("icerpc://host.zeroc.com:/identity", "/identity")]
        [TestCase("icerpc://com.zeroc.ice/identity?transport=iaps&option=a,b%2Cb,c&option=d")]
        [TestCase("icerpc://host.zeroc.com/identity?transport=100")]
        // leading :: to make the address IPv6-like
        [TestCase("icerpc://[::ab:cd:ef:00]/identity?transport=bt")]
        [TestCase("icerpc://host.zeroc.com:10000/identity?transport=tcp")]
        [TestCase("icerpc://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar")]
        [TestCase("icerpc://mylocation.domain.com/foo/bar?transport=loc", "/foo/bar")]
        [TestCase("icerpc://host:10000?transport=coloc")]
        [TestCase("icerpc:tcp -p 10000")]
        [TestCase("icerpc://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar")]
        [TestCase("icerpc://0.0.0.0/identity#facet")] // Any IPv4 in proxy endpoint (unusable but parses ok)
        [TestCase("icerpc://[::0]/identity#facet")] // Any IPv6 in proxy endpoint (unusable but parses ok)
        public void Proxy_Parse_ValidInputUriFormat(string str, string? path = null, string? fragment = null)
        {
            var proxy = Proxy.Parse(str);
            Assert.That(Proxy.TryParse(proxy.ToString(), invoker: null, format: null, out Proxy? proxy2), Is.True);

            if (path != null)
            {
                Assert.That(proxy.Path, Is.EqualTo(path));
            }

            if (fragment != null)
            {
                Assert.That(proxy.Fragment, Is.EqualTo(fragment));
            }

            Assert.AreEqual(proxy, proxy2); // round-trip works

            var prx = GreeterPrx.Parse(str);
            Assert.That(GreeterPrx.TryParse(prx.ToString(), invoker: null, format: null, out GreeterPrx prx2), Is.True);
            Assert.AreEqual(prx, prx2); // round-trip works
        }

        /// <summary>Tests that parsing an invalid proxies fails with <see cref="FormatException"/>.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        [TestCase("ice + tcp://host.zeroc.com:foo")] // missing host
        [TestCase("")]
        [TestCase("\"\"")]
        [TestCase("\"\" test")] // invalid trailing characters
        [TestCase("id@server test")]
        [TestCase("id -e A.0:tcp -h foobar")]
        [TestCase("id -f \"facet x")]
        [TestCase("id -f \'facet x")]
        [TestCase("test -f facet@test @test")]
        [TestCase("test -p 2.0")]
        [TestCase("icerpc://host/path?alt-endpoint=")] // alt-endpoint authority cannot be empty
        [TestCase("icerpc://host/path?alt-endpoint=/foo")] // alt-endpoint cannot have a path

        public void Proxy_Parse_InvalidInput(string str)
        {
            Assert.Catch<FormatException>(() => Proxy.Parse(str));
            Assert.Throws<FormatException>(() => Proxy.Parse(str, format: IceProxyFormat.Default));
            Assert.That(Proxy.TryParse(str, invoker: null, format: null, out _), Is.False);
            Assert.That(Proxy.TryParse(str, invoker: null, format: IceProxyFormat.Default, out _), Is.False);
        }

        [Test]
        public void Proxy_Equals()
        {
            Assert.That(Proxy.Equals(null, null), Is.True);
            var prx = Proxy.Parse("icerpc://host.zeroc.com/identity");
            Assert.That(Proxy.Equals(prx, prx), Is.True);
            Assert.That(Proxy.Equals(prx, Proxy.Parse("icerpc://host.zeroc.com/identity")), Is.True);
            Assert.That(Proxy.Equals(null, prx), Is.False);
            Assert.That(Proxy.Equals(prx, null), Is.False);
        }

        /// <summary>Test that proxies that are equal produce the same hash code.</summary>
        [TestCase("hello:tcp -h localhost")]
        [TestCase("icerpc://localhost/path?alt-endpoint=[::1]")]
        public void Proxy_HashCode(string proxyString)
        {
            IProxyFormat? format = proxyString.StartsWith("ice", StringComparison.Ordinal) ?
                null : IceProxyFormat.Default;
            var proxy1 = Proxy.Parse(proxyString, format: format);
            var proxy2 = proxy1.Clone();
            var proxy3 = Proxy.Parse(proxy2.ToString());

            CheckGetHashCode(proxy1, proxy2);
            CheckGetHashCode(proxy1, proxy3);

            static void CheckGetHashCode(Proxy p1, Proxy p2)
            {
                Assert.AreEqual(p1, p2);
                Assert.AreEqual(p1.GetHashCode(), p2.GetHashCode());
                // The second attempt should hit the hash code cache
                Assert.AreEqual(p1.GetHashCode(), p2.GetHashCode());
            }
        }

        /*
        // TODO: move this test to Slice
        [Test]
        public async Task Proxy_InvokeAsync()
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher, Greeter>()
                .BuildServiceProvider();

            var proxy = Proxy.FromConnection(serviceProvider.GetRequiredService<Connection>(), GreeterPrx.DefaultPath);

            IncomingResponse response =
                await proxy.InvokeAsync("ice_ping",
                                        proxy.Encoding,
                                        payloadSource: Encoding.Slice20.CreateEmptyPayload());

            Assert.DoesNotThrowAsync(async () => await response.CheckVoidReturnValueAsync(
                proxy.Invoker,
                IceDecoder.GetActivator(typeof(ProxyTests).Assembly),
                default));
        }
        */

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
            Assert.AreEqual(pipeline, received?.Proxy.Invoker);

            // Same with an endpoint
            service.Prx!.Value.Proxy.Endpoint = "icerpc://localhost";
            received = await prx.ReceiveProxyAsync();
            Assert.AreEqual(service.Prx?.Proxy.Endpoint, received?.Proxy.Endpoint);
            Assert.AreEqual(pipeline, received?.Proxy.Invoker);
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
                    router.UseProxyInvoker(pipeline);
                    return router;
                })
                .BuildServiceProvider();
            prx = ProxyTestPrx.FromConnection(serviceProvider2.GetRequiredService<Connection>());

            service.Prx = null;
            await prx.SendProxyAsync(prx);
            Assert.That(service.Prx, Is.Not.Null);
            Assert.AreEqual(pipeline, service.Prx?.Proxy.Invoker);
        }

        [Test]
        public void Proxy_UriOptions()
        {
            string proxyString = "icerpc://localhost:10000/test";

            var proxy = Proxy.Parse(proxyString);

            Assert.AreEqual("/test", proxy.Path);

            string complicated = $"{proxyString}?encoding=1.1&alt-endpoint=localhost";
            proxy = Proxy.Parse(complicated);

            Assert.AreEqual(Encoding.Slice11, proxy.Encoding);
            Endpoint altEndpoint = proxy.AltEndpoints[0];
            Assert.AreEqual(1, proxy.AltEndpoints.Count);
            Assert.AreEqual("tcp", altEndpoint.Transport);
        }

        [TestCase("1.3")]
        [TestCase("2.1")]
        public async Task Proxy_NotSupportedEncoding(string encoding)
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher, Greeter>()
                .BuildServiceProvider();

            var prx = GreeterPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());
            prx.Proxy.Encoding = Encoding.FromString(encoding);
            await prx.IcePingAsync(); // works fine, we use the protocol's encoding in this case
        }

        [Test]
        public async Task Proxy_FactoryMethodsAsync()
        {
            Assert.AreEqual("/IceRpc.Service", ServicePrx.DefaultPath);

            var proxy = Proxy.FromPath("/test");
            Assert.AreEqual("/test", proxy.Path);
            Assert.That(proxy.Endpoint, Is.Null);

            Assert.AreEqual("/IceRpc.Tests.Api.Greeter", GreeterPrx.DefaultPath);

            var greeter = GreeterPrx.FromPath("/test");
            Assert.AreEqual("/test", greeter.Proxy.Path);
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
            Assert.AreEqual(ServicePrx.DefaultPath, proxy.Path);
            Assert.AreEqual(connection, proxy.Connection);
            Assert.AreEqual(connection.RemoteEndpoint, proxy.Endpoint);

            greeter = GreeterPrx.FromConnection(connection);
            Assert.AreEqual(GreeterPrx.DefaultPath, greeter.Proxy.Path);
            Assert.AreEqual(connection, greeter.Proxy.Connection);
            Assert.AreEqual(connection.RemoteEndpoint, greeter.Proxy.Endpoint);

            await ServicePrx.FromConnection(connection).IcePingAsync();

            Assert.That(capture, Is.Not.Null);
            Assert.AreEqual(ServicePrx.DefaultPath, capture!.Service.Proxy.Path);
            Assert.AreEqual(capture.ServerConnection, capture.Service.Proxy.Connection);
            Assert.That(capture.Service.Proxy.Endpoint, Is.Null);

            Assert.AreEqual(GreeterPrx.DefaultPath, capture.Greeter.Proxy.Path);
            Assert.AreEqual(capture.ServerConnection, capture.Greeter.Proxy.Connection);
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
