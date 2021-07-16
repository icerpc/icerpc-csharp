// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Interop;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(30000)]
    public class ProxyTests
    {
        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Proxy_BuiltinOperationsAsync(Protocol protocol)
        {
            await using var server = new Server
            {
                Dispatcher = new Greeter(),
                Endpoint = TestHelper.GetUniqueColocEndpoint(protocol)
            };
            server.Listen();
            await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };
            await connection.ConnectAsync();

            var prx = GreeterPrx.FromConnection(connection);

            await prx.IcePingAsync();

            string[] ids = new string[]
            {
                "::Ice::Object",
                "::IceRpc::Tests::Api::Greeter",
            };
            CollectionAssert.AreEqual(ids, await prx.IceIdsAsync());

            Assert.That(await prx.IceIsAAsync("::IceRpc::Tests::Api::Greeter"), Is.True);
            Assert.That(await prx.IceIsAAsync("::IceRpc::Tests::Api::Foo"), Is.False);

            Assert.That(await prx.AsAsync<GreeterPrx>(), Is.Not.Null);

            // Test that builtin operation correctly forward the cancel param
            var canceled = new CancellationToken(canceled: true);
            Assert.ThrowsAsync<OperationCanceledException>(async () => await prx.IcePingAsync(cancel: canceled));
            Assert.ThrowsAsync<OperationCanceledException>(async () => await prx.IceIdsAsync(cancel: canceled));
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await prx.IceIsAAsync("::IceRpc::Tests::Api::Greeter", cancel: canceled));
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await prx.AsAsync<GreeterPrx>(cancel: canceled));

            // Test that builtin operation correctly forward the context
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
            var proxy = GreeterPrx.Parse(s).Proxy;

            proxy.Encoding = Encoding.V11;
            Assert.AreEqual(proxy.Encoding, Encoding.V11);
            proxy.Encoding = Encoding.V20;
            Assert.AreEqual(proxy.Encoding, Encoding.V20);

            if (proxy.Protocol == Protocol.Ice1)
            {
                var proxy2 = GreeterPrx.Parse("test:tcp -h localhost -p 10001").Proxy;
                proxy.Endpoint = proxy2.Endpoint;
                Assert.AreEqual(proxy.Endpoint, proxy2.Endpoint);
            }
            else
            {
                var proxy2 = GreeterPrx.Parse("ice+tcp://localhost:10001/test").Proxy;
                proxy.Endpoint = proxy2.Endpoint;
                Assert.AreEqual(proxy.Endpoint, proxy2.Endpoint);
            }

            if (proxy.Protocol == Protocol.Ice1)
            {
                Assert.AreEqual("facet", proxy.WithFacet("facet").GetFacet());
            }

            if (proxy.Protocol == Protocol.Ice1)
            {
                proxy = GreeterPrx.Parse(s).Proxy;

                Proxy other = proxy.WithPath("/test").WithFacet("facet");

                Assert.AreEqual("facet", other.GetFacet());
                Assert.AreEqual("test", other.GetIdentity().Name);
                Assert.AreEqual("", other.GetIdentity().Category);

                other = other.WithPath("/category/test");
                Assert.AreEqual("facet", other.GetFacet());
                Assert.AreEqual("test", other.GetIdentity().Name);
                Assert.AreEqual("category", other.GetIdentity().Category);

                other = proxy.WithPath("/foo").WithFacet("facet1");
                Assert.AreEqual("facet1", other.GetFacet());
                Assert.AreEqual("foo", other.GetIdentity().Name);
                Assert.AreEqual("", other.GetIdentity().Category);
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
        public void Proxy_Parse_ValidInputIce1Format(string str)
        {
            var proxy = Proxy.Parse(str);
            Assert.AreEqual(Protocol.Ice1, proxy.Protocol);
            Assert.That(Proxy.TryParse(proxy.ToString()!, invoker: null, out Proxy? proxy2), Is.True);
            Assert.AreEqual(proxy, proxy2); // round-trip works
        }

        [TestCase("ice+tcp://host.zeroc.com/identity#facet", "/identity%23facet")] // C# Uri parser escapes #
        [TestCase("ice+tcp://host.zeroc.com:1000/category/name")]
        [TestCase("ice+tcp://host.zeroc.com:1000/loc0/loc1/category/name")]
        [TestCase("ice+tcp://host.zeroc.com/category/name%20with%20space", "/category/name%20with%20space")]
        [TestCase("ice+tcp://host.zeroc.com/category/name with space", "/category/name%20with%20space")]
        [TestCase("ice+tcp://host.zeroc.com//identity")]
        [TestCase("ice+tcp://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com")]
        [TestCase("ice+tcp://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com:10000")]
        [TestCase("ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000,host2,host3,host4")]
        [TestCase("ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000&alt-endpoint=host2,host3&alt-endpoint=[::2]")]
        [TestCase("ice:location//identity#facet", "/location//identity%23facet")]
        [TestCase("ice+tcp://host.zeroc.com//identity")]
        [TestCase("ice+tcp://host.zeroc.com/\x7f€$%/!#$'()*+,:;=@[] %2F",
                  "/%7F%E2%82%AC$%25/!%23$'()*+,:;=@[]%20%2F")] // Only remarkable char is # converted into %23
        [TestCase(@"ice+tcp://host.zeroc.com/foo\bar\n\t!", "/foo/bar/n/t!")] // Parser converts \ to /
        // another syntax for empty port
        [TestCase("ice+tcp://host.zeroc.com:/identity", "/identity")]
        [TestCase("ice+universal://com.zeroc.ice/identity?transport=iaps&option=a,b%2Cb,c&option=d")]
        [TestCase("ice+universal://host.zeroc.com/identity?transport=100")]
        // leading :: to make the address IPv6-like
        [TestCase("ice+universal://[::ab:cd:ef:00]/identity?transport=bt")]
        [TestCase("ice+universal://host.zeroc.com:10000/identity?transport=tcp")]
        [TestCase("ice+universal://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar")]
        [TestCase("ice+loc://mylocation.domain.com/foo/bar", "/foo/bar")]
        [TestCase("ice+coloc://host:10000")]
        // a valid URI
        [TestCase("ice:tcp -p 10000")]
        // ice3 proxies
        [TestCase("ice+universal://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar&protocol=3")]
        [TestCase("ice+tcp://0.0.0.0/identity#facet")] // Any IPv4 in proxy endpoint (unusable but parses ok)
        [TestCase("ice+tcp://[::0]/identity#facet")] // Any IPv6 in proxy endpoint (unusable but parses ok)
        [TestCase("identity:tcp -h 0.0.0.0")] // Any IPv4 in proxy endpoint (unusable but parses ok)
        [TestCase("identity:tcp -h \"::0\"")] // Any IPv6 address in proxy endpoint (unusable but parses ok)
        [TestCase("identity:coloc -h *")]
        public void Proxy_Parse_ValidInputUriFormat(string str, string? path = null)
        {
            var proxy = Proxy.Parse(str);

            if (path != null)
            {
                Assert.AreEqual(path, proxy.Path);
            }

            var proxy2 = Proxy.Parse(proxy.ToString()!);
            Assert.AreEqual(proxy, proxy2); // round-trip works

            // Also try with non-default ToStringMode
            proxy2 = Proxy.Parse(proxy.ToString(ToStringMode.ASCII));
            Assert.AreEqual(proxy, proxy2);

            proxy2 = Proxy.Parse(proxy.ToString(ToStringMode.Compat));
            Assert.AreEqual(proxy, proxy2);
        }

        /// <summary>Tests that parsing an invalid proxies fails with <see cref="FormatException"/>.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        [TestCase("ice + tcp://host.zeroc.com:foo")] // missing host
        [TestCase("ice:identity?protocol=ice2")] // invalid protocol
        [TestCase("ice+universal://host.zeroc.com")] // missing transport
        [TestCase("ice+universal://host.zeroc.com:10000/identity?transport=tcp&protocol=ice1")] // invalid protocol
        [TestCase("ice://host:1000/identity")] // host not allowed
        [TestCase("ice+universal:/identity")] // missing host
        [TestCase("ice+tcp://host.zeroc.com/identity?protocol=3")] // unknown protocol (must use universal)
        [TestCase("ice+tcp://host.zeroc.com//identity?protocol=ice1")] // invalid protocol
        [TestCase("ice+tcp://host.zeroc.com/identity?alt-endpoint=host2?protocol=ice2")] // protocol option in alt-endpoint
        [TestCase("ice+tcp://host.zeroc.com/identity?foo=bar")] // unknown option
        [TestCase("ice+universal://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar&alt-endpoint=host2?transport=tcp$protocol=3")]
        [TestCase("")]
        [TestCase("\"\"")]
        [TestCase("\"\" test")] // invalid trailing characters
        [TestCase("test:")] // missing endpoint
        [TestCase("id@server test")]
        [TestCase("id -f \"facet x")]
        [TestCase("id -f \'facet x")]
        [TestCase("test -f facet@test @test")]
        [TestCase("test -p 2.0")]
        [TestCase("test:tcp@location")]
        [TestCase("test: :tcp")]
        [TestCase("id:opaque -t 99 -v abcd -x abc")] // invalid x option
        [TestCase("id:opaque")] // missing -t and -v
        [TestCase("id:opaque -t 1 -t 1 -v abcd")] // repeated -t
        [TestCase("id:opaque -t 1 -v abcd -v abcd")]
        [TestCase("id:opaque -v abcd")]
        [TestCase("id:opaque -t 1")]
        [TestCase("id:opaque -t -v abcd")]
        [TestCase("id:opaque -t 1 -v")]
        [TestCase("id:opaque -t x -v abcd")]
        [TestCase("id:opaque -t -1 -v abcd")] // -t must be >= 0
        [TestCase("id:opaque -t 99 -v x?c")] // invalid char in v
        [TestCase("id:opaque -t 99 -v xc")] // invalid length for base64 input
        [TestCase("id:loc -h foobar")] // cannot parse loc as a transport with ice1
        public void Proxy_Parse_InvalidInput(string str)
        {
            Assert.Throws<FormatException>(() => Proxy.Parse(str));
            Assert.That(Proxy.TryParse(str, invoker: null, out _), Is.False);
        }

        /// <summary>Test that the parsed proxy has the expected identity and location</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        /// <param name="name">The expected identity name for the parsed proxy.</param>
        /// <param name="category">The expected identity category for the parsed proxy.</param>
        /// <param name="location">The expected location for the parsed proxy.</param>
        [TestCase("test", "test", "")]
        [TestCase(" test ", "test", "")]
        [TestCase(" test", "test", "")]
        [TestCase("test ", "test", "")]
        [TestCase("'test -f facet'", "test -f facet", "")]
        [TestCase("\"test -f facet\"", "test -f facet", "")]
        [TestCase("\"test -f facet@test\"", "test -f facet@test", "")]
        [TestCase("\"test -f facet@test @test\"", "test -f facet@test @test", "")]
        [TestCase("test\\040test", "test test", "")]
        [TestCase("test\\40test", "test test", "")]
        // Test some octal and hex corner cases.
        [TestCase("test\\4test", "test\u0004test", "")]
        [TestCase("test\\04test", "test\u0004test", "")]
        [TestCase("test\\004test", "test\u0004test", "")]
        [TestCase("test\\1114test", "test\u00494test", "")]
        [TestCase("test\\b\\f\\n\\r\\t\\'\\\"\\\\test", "test\b\f\n\r\t\'\"\\test", "")]
        [TestCase("category/test", "test", "category")]
        public void Proxy_Parse_InputWithIdentity(string str, string name, string category)
        {
            var proxy = Proxy.Parse(str);
            Assert.AreEqual(name, proxy.GetIdentity().Name);
            Assert.AreEqual(category, proxy.GetIdentity().Category);
            Assert.AreEqual("", proxy.GetFacet());
        }

        [Test]
        public void Proxy_Equals()
        {
            Assert.That(Proxy.Equals(null, null));
            var prx = Proxy.Parse("ice+tcp://host.zeroc.com/identity");
            Assert.That(Proxy.Equals(prx, prx));
            Assert.That(Proxy.Equals(prx, Proxy.Parse("ice+tcp://host.zeroc.com/identity")));
            Assert.That(Proxy.Equals(null, prx), Is.False);
            Assert.That(Proxy.Equals(prx, null), Is.False);
        }

        [TestCase("ice+tcp://tcphost:10000/test?" +
                  "alt-endpoint=ice+universal://unihost:10000?transport=100$option=ABCD")]
        [TestCase("test -t:tcp -h tcphost -p 10000 -t 1200 -z " +
                  ": udp -h 239.255.1.1 -p 10001 --interface eth0 --ttl 5 " +
                  ":opaque -e 1.8 -t 100 -v ABCD")]
        public void Proxy_EndpointInformation(string prx)
        {
            var p1 = Proxy.Parse(prx);

            Endpoint? tcpEndpoint = p1.Endpoint;
            Assert.AreEqual(Transport.TCP, tcpEndpoint!.Transport);
            Assert.AreEqual(tcpEndpoint.Protocol == Protocol.Ice1 ? false : null, tcpEndpoint.IsSecure);
            Assert.AreEqual("tcphost", tcpEndpoint.Host);
            Assert.AreEqual(10000, tcpEndpoint.Port);

            if (p1.Protocol == Protocol.Ice1)
            {
                Assert.AreEqual("1200", tcpEndpoint["timeout"]);
                Assert.AreEqual("true", tcpEndpoint["compress"]);
            }
            Assert.That(tcpEndpoint.IsDatagram, Is.False);

            if (p1.Protocol == Protocol.Ice1)
            {
                Endpoint udpEndpoint = p1.AltEndpoints[0];
                Assert.AreEqual("239.255.1.1", udpEndpoint.Host);
                Assert.AreEqual(10001, udpEndpoint.Port);
                Assert.AreEqual("eth0", udpEndpoint["interface"]);
                Assert.AreEqual("5", udpEndpoint["ttl"]);
                Assert.AreEqual(null, udpEndpoint["timeout"]);
                Assert.AreEqual(null, udpEndpoint["compress"]);
                Assert.That(udpEndpoint.IsSecure, Is.False);
                Assert.That(udpEndpoint.IsDatagram);
                Assert.AreEqual(Transport.UDP, udpEndpoint.Transport);

                Endpoint opaqueEndpoint = p1.AltEndpoints[1];
                Assert.AreEqual("ABCD", opaqueEndpoint["value"]);
                Assert.AreEqual("1.8", opaqueEndpoint["value-encoding"]);
            }
            else
            {
                Endpoint universalEndpoint = p1.AltEndpoints[0];
                Assert.AreEqual((Transport)100, universalEndpoint.Transport);
                Assert.AreEqual("ABCD", universalEndpoint["option"]);
            }
        }

        /// <summary>Test that proxies that are equal produce the same hash code.</summary>
        [TestCase("hello:tcp -h localhost")]
        [TestCase("ice+tcp://localhost/path?alt-endpoint=ice+tcp://[::1]")]
        public void Proxy_HashCode(string proxyString)
        {
            var proxy1 = Proxy.Parse(proxyString);
            var proxy2 = proxy1.Clone();
            var proxy3 = Proxy.Parse(proxy2.ToString()!);

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
            await using var server = new Server
            {
                Dispatcher = new Greeter(),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };
            var prx = GreeterPrx.FromConnection(connection);

            (ReadOnlyMemory<byte> payload, IceRpc.RpcStreamReader? _, Encoding payloadEncoding, Connection responseConnection) =
                await prx.Proxy.InvokeAsync("SayHello", Payload.FromEmptyArgs(prx.Proxy));

            Assert.DoesNotThrow(() => payload.CheckVoidReturnValue(payloadEncoding));
        }

        [Test]
        public async Task Proxy_ReceiveProxyAsync()
        {
            var service = new ProxyTest();

            await using var server = new Server
            {
                Dispatcher = service,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };
            var prx = ProxyTestPrx.FromConnection(connection);

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
            await using var server = new Server
            {
                Dispatcher = service,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            server.Listen();

            await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };
            var prx = ProxyTestPrx.FromConnection(connection);
            await prx.SendProxyAsync(prx);
            Assert.That(service.Prx, Is.Not.Null);
            Assert.That(service.Prx?.Proxy.Invoker, Is.Null);

            // Now with a router and the ProxyInvoker middleware - we set the invoker on the proxy received by the
            // service.
            var router = new Router();
            router.Map<IProxyTest>(service);

            var pipeline = new Pipeline();
            router.Use(Middleware.ProxyInvoker(pipeline));

            server.Dispatcher = router;
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

            Assert.AreEqual(Encoding.V11, proxy.Encoding);
            Endpoint altEndpoint = proxy.AltEndpoints[0];
            Assert.AreEqual(1, proxy.AltEndpoints.Count);
            Assert.AreEqual(Transport.TCP, altEndpoint.Transport);
        }

        [TestCase("1.3")]
        [TestCase("2.1")]
        public void Proxy_NotSupportedEncoding(string encoding)
        {
            var pipeline = new Pipeline();
            var prx = GreeterPrx.Parse("/test", pipeline);
            prx.Proxy.Encoding = Encoding.Parse(encoding);
            Assert.ThrowsAsync<NotSupportedException>(async () => await prx.IcePingAsync());
        }

        [TestCase("3")]
        [TestCase("4")]
        public async Task Proxy_NotSupportedProtocol(string protocol)
        {
            await using var connection = new Connection
            {
                RemoteEndpoint = $"ice+universal://localhost?transport=tcp&protocol={protocol}"
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
            var router = new Router();
            router.Use(next => new InlineDispatcher((request, cancel) =>
            {
                capture = new
                {
                    ServerConnection = request.Connection,
                    Service = ServicePrx.FromConnection(request.Connection),
                    Greeter = GreeterPrx.FromConnection(request.Connection)
                };
                return new(new OutgoingResponse(request, Payload.FromVoidReturnValue(request)));
            }));

            await using var server = new Server
            {
                Endpoint = "ice+tcp://127.0.0.1:0?tls=false",
                // TODO use localhost see https://github.com/dotnet/runtime/issues/53447
                HostName = "127.0.0.1",
                Dispatcher = router
            };
            server.Listen();
            await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };

            proxy = Proxy.FromConnection(connection, ServicePrx.DefaultPath);
            Assert.AreEqual(ServicePrx.DefaultPath, proxy.Path);
            Assert.AreEqual(connection, proxy.Connection);
            Assert.AreEqual(connection.RemoteEndpoint, proxy.Endpoint);

            greeter = GreeterPrx.FromConnection(connection);
            Assert.AreEqual(GreeterPrx.DefaultPath, greeter.Proxy.Path);
            Assert.AreEqual(connection, greeter.Proxy.Connection);
            Assert.AreEqual(connection.RemoteEndpoint, greeter.Proxy.Endpoint);

            proxy = Proxy.FromServer(server, ServicePrx.DefaultPath);
            Assert.AreEqual(ServicePrx.DefaultPath, proxy.Path);
            Assert.That(proxy.Connection, Is.Null);
            Assert.AreEqual(server.ProxyEndpoint, proxy.Endpoint);

            greeter = GreeterPrx.FromServer(server);
            Assert.AreEqual(GreeterPrx.DefaultPath, greeter.Proxy.Path);
            Assert.That(greeter.Proxy.Connection, Is.Null);
            Assert.AreEqual(server.ProxyEndpoint, greeter.Proxy.Endpoint);

            await ServicePrx.FromConnection(connection).IcePingAsync();

            Assert.That(capture, Is.Not.Null);
            Assert.AreEqual(ServicePrx.DefaultPath, capture.Service.Proxy.Path);
            Assert.AreEqual(capture.ServerConnection, capture.Service.Proxy.Connection);
            Assert.That(capture.Service.Proxy.Endpoint, Is.Null);

            Assert.AreEqual(GreeterPrx.DefaultPath, capture.Greeter.Proxy.Path);
            Assert.AreEqual(capture.ServerConnection, capture.Greeter.Proxy.Connection);
            Assert.That(capture.Greeter.Proxy.Endpoint, Is.Null);
        }

        private class Greeter : Service, IGreeter
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                default;
        }

        private class ProxyTest : Service, IProxyTest
        {
            internal ProxyTestPrx? Prx { get; set; }

            public ValueTask<ProxyTestPrx?> ReceiveProxyAsync(Dispatch dispatch, CancellationToken cancel) =>
                new(Prx);

            public ValueTask SendProxyAsync(ProxyTestPrx prx, Dispatch dispatch, CancellationToken cancel)
            {
                Prx = prx;
                return default;
            }
        }
    }
}
