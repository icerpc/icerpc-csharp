// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(5000)]
    [Log(LogAttributeLevel.Information)]
    public class ProxyTests
    {
        [TestCase(ProtocolCode.Ice1)]
        [TestCase(ProtocolCode.Ice2)]
        public async Task Proxy_ServiceAsync(ProtocolCode protocol)
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

        [TestCase("ice+tcp://localhost:10000/test")]
        [TestCase("test:tcp -h localhost -p 10000")]
        public void Proxy_SetProperty(string s)
        {
            var proxy = Proxy.Parse(s);

            proxy.Encoding = Encoding.Ice11;
            Assert.AreEqual(Encoding.Ice11, proxy.Encoding);
            proxy.Encoding = Encoding.Ice20;
            Assert.AreEqual(Encoding.Ice20, proxy.Encoding);

            if (proxy.Protocol == Protocol.Ice1)
            {
                var proxy2 = Proxy.Parse("test:tcp -h localhost -p 10001");
                proxy.Endpoint = proxy2.Endpoint;
                Assert.AreEqual(proxy.Endpoint, proxy2.Endpoint);
            }
            else
            {
                var proxy2 = Proxy.Parse("ice+tcp://localhost:10001/test");
                proxy.Endpoint = proxy2.Endpoint;
                Assert.AreEqual(proxy.Endpoint, proxy2.Endpoint);
            }
        }

        [Test]
        public void Proxy_SetProperty_ArgumentException()
        {
            var ice1Proxy = Proxy.Parse("hello:tcp -h localhost -p 10000");
            Assert.AreEqual(Protocol.Ice1, ice1Proxy.Protocol);
            var ice2Proxy = Proxy.Parse("ice+tcp://host.zeroc.com/hello");
            Assert.AreEqual(Protocol.Ice2, ice2Proxy.Protocol);

            // Endpoints protocol must match the proxy protocol
            Assert.Throws<ArgumentException>(() => ice1Proxy.Endpoint = ice2Proxy.Endpoint);
            Assert.Throws<ArgumentException>(() => ice2Proxy.Endpoint = ice1Proxy.Endpoint);
        }

        /// <summary>Test the parsing of valid proxies.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        [TestCase("ice -t:tcp -h localhost -p 10000")]
        [TestCase("ice+tcp:ssl -h localhost -p 10000")]
        [TestCase("identity:tcp -h 0.0.0.0")] // Any IPv4 in proxy endpoint (unusable but parses ok)
        [TestCase("identity:tcp -h \"::0\"")] // Any IPv6 address in proxy endpoint (unusable but parses ok)
        [TestCase("identity:coloc -h *")]
        [TestCase("identity -e 4.5:coloc -h *")]
        [TestCase("name -f facet:coloc -h localhost", "/name", "facet")]
        [TestCase("category/name -f facet:coloc -h localhost", "/category/name", "facet")]
        [TestCase("cat$gory/nam$ -f fac$t:coloc -h localhost", "/cat%24gory/nam%24", "fac$t")]
        public void Proxy_Parse_ValidInputIce1Format(string str, string? path = null, string? fragment = null)
        {
            var proxy = Proxy.Parse(str);

            if (path != null)
            {
                Assert.AreEqual(path, proxy.Path);
            }

            if (fragment != null)
            {
                Assert.That(proxy.Fragment, Is.EqualTo(fragment));
            }

            Assert.AreEqual(Protocol.Ice1, proxy.Protocol);
            Assert.That(Proxy.TryParse(proxy.ToString(), invoker: null, parser: null, out Proxy? proxy2), Is.True);
            Assert.AreEqual(proxy, proxy2); // round-trip works

            // Also try with non-default ToStringMode
            proxy2 = Proxy.Parse(proxy.ToString(ToStringMode.ASCII));
            Assert.AreEqual(proxy, proxy2);

            proxy2 = Proxy.Parse(proxy.ToString(ToStringMode.Compat));
            Assert.AreEqual(proxy, proxy2);

            var prx = GreeterPrx.Parse(str);
            Assert.AreEqual(Protocol.Ice1, prx.Proxy.Protocol);
            Assert.That(GreeterPrx.TryParse(prx.ToString(), invoker: null, parser: null, out GreeterPrx prx2), Is.True);
            Assert.AreEqual(prx, prx2); // round-trip works

            var identity = Identity.FromPath(prx.Proxy.Path);
            var identity2 = Identity.FromPath(prx2.Proxy.Path);
            Assert.AreEqual(identity, identity2);
            Assert.AreEqual(prx.Proxy.Fragment, prx2.Proxy.Fragment); // facets
        }

        [TestCase("ice+tcp://host.zeroc.com/path?encoding=foo")]
        [TestCase("ice+tcp://host.zeroc.com/identity#facet", "/identity")]
        [TestCase("ice+tcp://host.zeroc.com/identity?protocol=ice1&tls=false")]
        [TestCase("ice+tcp://host.zeroc.com/identity?protocol=ice1&tls=true")] // TODO: add no tls test
        [TestCase("ice+tcp://host.zeroc.com:1000/category/name")]
        [TestCase("ice+tcp://host.zeroc.com:1000/loc0/loc1/category/name")]
        [TestCase("ice+tcp://host.zeroc.com/category/name%20with%20space", "/category/name%20with%20space")]
        [TestCase("ice+tcp://host.zeroc.com/category/name with space", "/category/name%20with%20space")]
        [TestCase("ice+tcp://host.zeroc.com//identity")]
        [TestCase("ice+tcp://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com")]
        [TestCase("ice+tcp://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com:10000")]
        [TestCase("ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000,host2,host3,host4")]
        [TestCase("ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000&alt-endpoint=host2,host3&alt-endpoint=[::2]")]
        [TestCase("ice:location//identity#facet", "/location//identity")]
        [TestCase("ice+tcp://host.zeroc.com//identity")]
        [TestCase("ice+tcp://host.zeroc.com/\x7f€$%/!#$'()*+,:;=@[] %2F", "/%7F%E2%82%AC$%25/!")]
        [TestCase(@"ice+tcp://host.zeroc.com/foo\bar\n\t!", "/foo/bar/n/t!")] // Parser converts \ to /
        // another syntax for empty port
        [TestCase("ice+tcp://host.zeroc.com:/identity", "/identity")]
        [TestCase("ice+foo://com.zeroc.ice/identity?transport=iaps&option=a,b%2Cb,c&option=d")]
        [TestCase("ice+foo://host.zeroc.com/identity?transport=100")]
        // leading :: to make the address IPv6-like
        [TestCase("ice+foo://[::ab:cd:ef:00]/identity?transport=bt")]
        [TestCase("ice+foo://host.zeroc.com:10000/identity?transport=tcp")]
        [TestCase("ice+foo://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar")]
        [TestCase("ice+loc://mylocation.domain.com/foo/bar", "/foo/bar")]
        [TestCase("ice+coloc://host:10000")]
        [TestCase("ice:tcp -p 10000")]
        // ice3 proxies
        [TestCase("ice+foo://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar&protocol=ice3")]
        [TestCase("ice+tcp://0.0.0.0/identity#facet")] // Any IPv4 in proxy endpoint (unusable but parses ok)
        [TestCase("ice+tcp://[::0]/identity#facet")] // Any IPv6 in proxy endpoint (unusable but parses ok)
        public void Proxy_Parse_ValidInputUriFormat(string str, string? path = null)
        {
            var proxy = Proxy.Parse(str);
            Assert.That(Proxy.TryParse(proxy.ToString(), invoker: null, parser: null, out Proxy? proxy2), Is.True);

            Assert.AreEqual(proxy, proxy2); // round-trip works

            var prx = GreeterPrx.Parse(str);
            Assert.That(GreeterPrx.TryParse(prx.ToString(), invoker: null, parser: null, out GreeterPrx prx2), Is.True);
            Assert.AreEqual(prx, prx2); // round-trip works
        }

        /// <summary>Tests that parsing an invalid proxies fails with <see cref="FormatException"/>.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        [TestCase("ice + tcp://host.zeroc.com:foo")] // missing host

        [TestCase("ice://host:1000/identity")] // host not allowed
        [TestCase("ice+foo:/identity")] // missing host
        [TestCase("ice+tcp://host.zeroc.com//identity?protocol=5")] // invalid protocol
        [TestCase("ice+foo://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar&alt-endpoint=host2?transport=tcp$protocol=3")]
        [TestCase("")]
        [TestCase("\"\"")]
        [TestCase("\"\" test")] // invalid trailing characters
        [TestCase("test:")] // missing endpoint
        [TestCase("id@server test")]
        [TestCase("id -e A.0:tcp -h foobar")]
        [TestCase("id -f \"facet x")]
        [TestCase("id -f \'facet x")]
        [TestCase("test -f facet@test @test")]
        [TestCase("test -p 2.0")]
        [TestCase("test:tcp@location")]
        [TestCase("test: :tcp")]
        [TestCase("id:loc -h foobar")] // cannot parse loc as a transport with ice1
        public void Proxy_Parse_InvalidInput(string str)
        {
            Assert.Throws<FormatException>(() => Proxy.Parse(str));
            Assert.That(Proxy.TryParse(str, invoker: null, parser: null, out _), Is.False);
        }

        [Test]
        public void Proxy_Equals()
        {
            Assert.That(Proxy.Equals(null, null), Is.True);
            var prx = Proxy.Parse("ice+tcp://host.zeroc.com/identity");
            Assert.That(Proxy.Equals(prx, prx), Is.True);
            Assert.That(Proxy.Equals(prx, Proxy.Parse("ice+tcp://host.zeroc.com/identity")), Is.True);
            Assert.That(Proxy.Equals(null, prx), Is.False);
            Assert.That(Proxy.Equals(prx, null), Is.False);
        }

        /// <summary>Test that proxies that are equal produce the same hash code.</summary>
        [TestCase("hello:tcp -h localhost")]
        [TestCase("ice+tcp://localhost/path?alt-endpoint=ice+tcp://[::1]")]
        public void Proxy_HashCode(string proxyString)
        {
            var proxy1 = Proxy.Parse(proxyString);
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
                                        payloadSource: Encoding.Ice20.CreateEmptyPayload());

            Assert.DoesNotThrowAsync(async () => await response.CheckVoidReturnValueAsync(
                proxy.Invoker,
                IceDecoder.GetActivator(typeof(ProxyTests).Assembly),
                default));
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
            Assert.That(received?.Proxy.Invoker, Is.Null);

            var pipeline = new Pipeline();
            prx.Proxy.Invoker = pipeline;
            received = await prx.ReceiveProxyAsync();
            Assert.AreEqual(pipeline, received?.Proxy.Invoker);

            // Same with an endpoint
            service.Prx!.Value.Proxy.Endpoint = "ice+tcp://localhost";
            received = await prx.ReceiveProxyAsync();
            Assert.AreEqual(service.Prx?.Proxy.Endpoint, received?.Proxy.Endpoint);
            Assert.AreEqual(pipeline, received?.Proxy.Invoker);
        }

        [Test]
        public async Task Proxy_SendProxyAsync()
        {
            var service = new ProxyTest();

            // First verify that the invoker of a proxy received over an incoming request is by default null.
            await using ServiceProvider serviceProvider1 = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ => service)
                .BuildServiceProvider();

            var prx = ProxyTestPrx.FromConnection(serviceProvider1.GetRequiredService<Connection>());
            await prx.SendProxyAsync(prx);
            Assert.That(service.Prx, Is.Not.Null);
            Assert.That(service.Prx?.Proxy.Invoker, Is.Null);

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
            string proxyString = "ice+tcp://localhost:10000/test";

            var proxy = Proxy.Parse(proxyString);

            Assert.AreEqual("/test", proxy.Path);

            string complicated = $"{proxyString}?encoding=1.1&alt-endpoint=ice+tcp://localhost";
            proxy = Proxy.Parse(complicated);

            Assert.AreEqual(Encoding.Ice11, proxy.Encoding);
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

        [TestCase("ice3")]
        [TestCase("ice4")]
        public async Task Proxy_NotSupportedProtocol(string protocol)
        {
            await using var connection = new Connection
            {
                RemoteEndpoint = $"ice+tcp://localhost?transport=tcp&protocol={protocol}"
            };

            var prx = GreeterPrx.FromConnection(connection);
            Assert.ThrowsAsync<NotSupportedException>(async () => await prx.IcePingAsync());
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
