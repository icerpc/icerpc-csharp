// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    public class ProxyTests : ColocTest
    {
        [Test]
        public async Task Proxy_BuiltinOperationsAsync()
        {
            await using var communicator = new Communicator();
            await using var server = new Server
            {
                Invoker = communicator,
                Dispatcher = new GreeterService(),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            server.Listen();
            IGreeterServicePrx? prx = server.CreateProxy<IGreeterServicePrx>("/test");

            await prx.IcePingAsync();

            Assert.AreEqual("::IceRpc::Tests::Api::GreeterService", await prx.IceIdAsync());

            string[] ids = new string[]
            {
                "::Ice::Object",
                "::IceRpc::Tests::Api::GreeterService",
            };
            CollectionAssert.AreEqual(ids, await prx.IceIdsAsync());

            Assert.That(await prx.IceIsAAsync("::IceRpc::Tests::Api::GreeterService"), Is.True);
            Assert.That(await prx.IceIsAAsync("::IceRpc::Tests::Api::Foo"), Is.False);

            Assert.IsNotNull(await prx.As<IServicePrx>().CheckedCastAsync<IGreeterServicePrx>());

            // Test that builtin operation correctly forward the cancel param
            var canceled = new CancellationToken(canceled: true);
            Assert.ThrowsAsync<OperationCanceledException>(async () => await prx.IcePingAsync(cancel: canceled));
            Assert.ThrowsAsync<OperationCanceledException>(async () => await prx.IceIdAsync(cancel: canceled));
            Assert.ThrowsAsync<OperationCanceledException>(async () => await prx.IceIdsAsync(cancel: canceled));
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await prx.IceIsAAsync("::IceRpc::Tests::Api::GreeterService", cancel: canceled));
            Assert.ThrowsAsync<OperationCanceledException>(
                async () => await prx.As<IServicePrx>().CheckedCastAsync<IGreeterServicePrx>(cancel: canceled));

            // Test that builtin operation correctly forward the context
            var invocation = new Invocation
            {
                Context = new() { ["foo"] = "bar" }
            };

            await using var pool = new Communicator();
            prx.Invoker = pool;
            pool.Use(next => new InlineInvoker((request, cancel) =>
            {
                Assert.AreEqual(request.Context, invocation.Context);
                return next.InvokeAsync(request, cancel);
            }));

            await prx.IcePingAsync(invocation);
            await prx.IceIdAsync(invocation);
            await prx.IceIdsAsync(invocation);
            await prx.IceIsAAsync("::IceRpc::Tests::Api::GreeterService", invocation);
            await prx.As<IServicePrx>().CheckedCastAsync<IGreeterServicePrx>(invocation);
        }

        [TestCase("ice+tcp://localhost:10000/test")]
        [TestCase("test:tcp -h localhost -p 10000")]
        public async Task Proxy_SetProperty(string s)
        {
            await using var communicator = new Communicator();
            var prx = IGreeterServicePrx.Parse(s, communicator);

            prx.CacheConnection = false;
            Assert.IsFalse(prx.CacheConnection);
            prx.CacheConnection = true;
            Assert.IsTrue(prx.CacheConnection);

            prx.Context = new Dictionary<string, string>
            {
                { "key1", "value1" },
                { "key2", "value2" },
            };
            Assert.AreEqual(2, prx.Context.Count);
            Assert.AreEqual("value1", prx.Context["key1"]);
            Assert.AreEqual("value2", prx.Context["key2"]);

            prx.Encoding = Encoding.V11;
            Assert.AreEqual(prx.Encoding, Encoding.V11);
            prx.Encoding = Encoding.V20;
            Assert.AreEqual(prx.Encoding, Encoding.V20);

            if (prx.Protocol == Protocol.Ice1)
            {
                var prx2 = IGreeterServicePrx.Parse("test:tcp -h localhost -p 10001", communicator);
                prx.Endpoint = prx2.Endpoint;
                Assert.AreEqual(prx.Endpoint, prx2.Endpoint);
            }
            else
            {
                var prx2 = IGreeterServicePrx.Parse("ice+tcp://localhost:10001/test", communicator);
                prx.Endpoint = prx2.Endpoint;
                Assert.AreEqual(prx.Endpoint, prx2.Endpoint);
            }

            if (prx.Protocol == Protocol.Ice1)
            {
                Assert.AreEqual("facet", prx.WithFacet<IGreeterServicePrx>("facet").GetFacet());
            }

            var server = new Server
            {
                Invoker = communicator,
                Dispatcher = new GreeterService(),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            server.Listen();
            prx = server.CreateProxy<IGreeterServicePrx>("/");
            var connection = await prx.GetConnectionAsync();

            prx = IGreeterServicePrx.Parse(s, communicator);

            prx.InvocationTimeout = TimeSpan.FromMilliseconds(10);
            Assert.AreEqual(prx.InvocationTimeout, TimeSpan.FromMilliseconds(10));

            prx.PreferExistingConnection = false;
            Assert.IsFalse(prx.PreferExistingConnection);
            prx.PreferExistingConnection = true;
            Assert.IsTrue(prx.PreferExistingConnection);

            prx.IsOneway = false;
            Assert.IsFalse(prx.IsOneway);
            prx.IsOneway = true;
            Assert.IsTrue(prx.IsOneway);

            if (prx.Protocol == Protocol.Ice1)
            {
                IGreeterServicePrx other =
                    prx.WithPath<IGreeterServicePrx>("/test").WithFacet<IGreeterServicePrx>("facet");

                Assert.AreEqual("facet", other.GetFacet());
                Assert.AreEqual("test", other.GetIdentity().Name);
                Assert.AreEqual("", other.GetIdentity().Category);

                other = other.WithPath<IGreeterServicePrx>("/category/test");
                Assert.AreEqual("facet", other.GetFacet());
                Assert.AreEqual("test", other.GetIdentity().Name);
                Assert.AreEqual("category", other.GetIdentity().Category);

                other = prx.WithPath<IGreeterServicePrx>("/foo").WithFacet<IGreeterServicePrx>("facet1");
                Assert.AreEqual("facet1", other.GetFacet());
                Assert.AreEqual("foo", other.GetIdentity().Name);
                Assert.AreEqual("", other.GetIdentity().Category);
            }
        }

        [Test]
        public void Proxy_SetProperty_ArgumentException()
        {
            var prxIce1 = IServicePrx.Parse("hello:tcp -h localhost -p 10000", Communicator);
            Assert.AreEqual(Protocol.Ice1, prxIce1.Protocol);
            var prxIce2 = IServicePrx.Parse("ice+tcp://host.zeroc.com/hello", Communicator);
            Assert.AreEqual(Protocol.Ice2, prxIce2.Protocol);

            // Endpoints protocol must match the proxy protocol
            Assert.Throws<ArgumentException>(() => prxIce1.Endpoint = prxIce2.Endpoint);
            Assert.Throws<ArgumentException>(() => prxIce2.Endpoint = prxIce1.Endpoint);

            // Zero is not a valid invocation timeout
            Assert.Throws<ArgumentException>(() => prxIce2.InvocationTimeout = TimeSpan.Zero);
        }

        /// <summary>Test the parsing of valid proxies.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        [TestCase("ice -t:tcp -h localhost -p 10000")]
        [TestCase("ice+tcp:ssl -h localhost -p 10000")]
        public void Proxy_Parse_ValidInputIce1Format(string str)
        {
            var prx = IServicePrx.Parse(str, Communicator);
            Assert.AreEqual(Protocol.Ice1, prx.Protocol);
            Assert.IsTrue(IServicePrx.TryParse(prx.ToString()!, Communicator, out IServicePrx? prx2));
            Assert.AreEqual(prx, prx2); // round-trip works
        }

        [TestCase("ice+tcp://host.zeroc.com/identity#facet", "/identity%23facet")] // C# Uri parser escapes #
        [TestCase("ice+tcp://host.zeroc.com:1000/category/name")]
        [TestCase("ice+tcp://host.zeroc.com:1000/loc0/loc1/category/name")]
        [TestCase("ice+tcp://host.zeroc.com/category/name%20with%20space", "/category/name%20with%20space")]
        [TestCase("ice+tcp://host.zeroc.com/category/name with space", "/category/name%20with%20space")]
        [TestCase("ice+ws://host.zeroc.com//identity")]
        [TestCase("ice+ws://host.zeroc.com//identity?invocation-timeout=100ms", "//identity")]
        [TestCase("ice+ws://host.zeroc.com//identity?invocation-timeout=1s")]
        [TestCase("ice+ws://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com")]
        [TestCase("ice+ws://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com:10000")]
        [TestCase("ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000,host2,host3,host4")]
        [TestCase("ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000&alt-endpoint=host2,host3&alt-endpoint=[::2]")]
        [TestCase("ice:location//identity#facet", "/location//identity%23facet")]
        [TestCase("ice+tcp://host.zeroc.com//identity")]
        [TestCase("ice+tcp://host.zeroc.com/\x7f€$%/!#$'()*+,:;=@[] %2F?invocation-timeout=100ms",
                  "/%7F%E2%82%AC$%25/!%23$'()*+,:;=@[]%20%2F")] // Only remarkable char is # converted into %23
        [TestCase(@"ice+tcp://host.zeroc.com/foo\bar\n\t!", "/foo/bar/n/t!")] // Parser converts \ to /
        // another syntax for empty port
        [TestCase("ice+tcp://host.zeroc.com:/identity", "/identity")]
        [TestCase("ice+universal://com.zeroc.ice/identity?transport=iaps&option=a,b%2Cb,c&option=d")]
        [TestCase("ice+universal://host.zeroc.com/identity?transport=100")]
        // leading :: to make the address IPv6-like
        [TestCase("ice+universal://[::ab:cd:ef:00]/identity?transport=bt")]
        [TestCase("ice+ws://host.zeroc.com/identity?resource=/foo%2Fbar?/xyz")]
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
            var prx = IServicePrx.Parse(str, Communicator);

            if (path != null)
            {
                Assert.AreEqual(path, prx.Path);
            }

            var prx2 = IServicePrx.Parse(prx.ToString()!, Communicator);
            Assert.AreEqual(prx, prx2); // round-trip works

            // Also try with non-default ToStringMode
            prx2 = IServicePrx.Parse(prx.ToString(ToStringMode.ASCII), Communicator);
            Assert.AreEqual(prx, prx2);

            prx2 = IServicePrx.Parse(prx.ToString(ToStringMode.Compat), Communicator);
            Assert.AreEqual(prx, prx2);
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
        [TestCase("ice+ws://host.zeroc.com//identity?protocol=ice1")] // invalid protocol
        [TestCase("ice+tcp://host.zeroc.com/identity?alt-endpoint=host2?protocol=ice2")] // protocol option in alt-endpoint
        [TestCase("ice+tcp://host.zeroc.com/identity?foo=bar")] // unknown option
        [TestCase("ice+tcp://host.zeroc.com/identity?invocation-timeout=0s")] // 0 is not a valid invocation timeout
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
            Assert.Throws<FormatException>(() => IServicePrx.Parse(str, Communicator));
            Assert.IsFalse(IServicePrx.TryParse(str, Communicator, out _));
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
            var prx = IServicePrx.Parse(str, Communicator);
            Assert.AreEqual(name, prx.GetIdentity().Name);
            Assert.AreEqual(category, prx.GetIdentity().Category);
            Assert.AreEqual("", prx.GetFacet());
        }

        [Test]
        public void Proxy_Equals()
        {
            Assert.IsTrue(IServicePrx.Equals(null, null));
            var prx = IServicePrx.Parse("ice+tcp://host.zeroc.com/identity", Communicator);
            Assert.IsTrue(IServicePrx.Equals(prx, prx));
            Assert.IsTrue(IServicePrx.Equals(prx, IServicePrx.Parse("ice+tcp://host.zeroc.com/identity", Communicator)));
            Assert.IsFalse(IServicePrx.Equals(null, prx));
            Assert.IsFalse(IServicePrx.Equals(prx, null));
        }

        [TestCase("ice+tcp://tcphost:10000/test?" +
                  "alt-endpoint=ice+universal://unihost:10000?transport=100$option=ABCD")]
        [TestCase("test -t:tcp -h tcphost -p 10000 -t 1200 -z " +
                  ": udp -h 239.255.1.1 -p 10001 --interface eth0 --ttl 5 " +
                  ":opaque -e 1.8 -t 100 -v ABCD")]
        public void Proxy_EndpointInformation(string prx)
        {
            var p1 = IServicePrx.Parse(prx, Communicator);

            var tcpEndpoint = p1.Endpoint;
            Assert.AreEqual(Transport.TCP, tcpEndpoint!.Transport);
            Assert.AreEqual(tcpEndpoint.Protocol == Protocol.Ice1 ? false : null, tcpEndpoint.IsSecure);
            Assert.AreEqual("tcphost", tcpEndpoint.Host);
            Assert.AreEqual(10000, tcpEndpoint.Port);

            if (p1.Protocol == Protocol.Ice1)
            {
                Assert.AreEqual("1200", tcpEndpoint["timeout"]);
                Assert.AreEqual("true", tcpEndpoint["compress"]);
            }
            Assert.IsFalse(tcpEndpoint.IsDatagram);

            if (p1.Protocol == Protocol.Ice1)
            {
                var udpEndpoint = p1.AltEndpoints[0];
                Assert.AreEqual("239.255.1.1", udpEndpoint.Host);
                Assert.AreEqual(10001, udpEndpoint.Port);
                Assert.AreEqual("eth0", udpEndpoint["interface"]);
                Assert.AreEqual("5", udpEndpoint["ttl"]);
                Assert.AreEqual(null, udpEndpoint["timeout"]);
                Assert.AreEqual(null, udpEndpoint["compress"]);
                Assert.IsFalse(udpEndpoint.IsSecure);
                Assert.IsTrue(udpEndpoint.IsDatagram);
                Assert.AreEqual(Transport.UDP, udpEndpoint.Transport);

                var opaqueEndpoint = p1.AltEndpoints[1];
                Assert.AreEqual("ABCD", opaqueEndpoint["value"]);
                Assert.AreEqual("1.8", opaqueEndpoint["value-encoding"]);
            }
            else
            {
                var universalEndpoint = p1.AltEndpoints[0];
                Assert.AreEqual((Transport)100, universalEndpoint.Transport);
                Assert.AreEqual("ABCD", universalEndpoint["option"]);
            }
        }

        /// <summary>Test that proxies that are equal produce the same hash code.</summary>
        [TestCase("hello:tcp -h localhost")]
        [TestCase("ice+tcp://localhost/path?invocation-timeout=10s&cache-connection=false&alt-endpoint=ice+ws://[::1]")]
        public void Proxy_HashCode(string proxyString)
        {
            var prx1 = IServicePrx.Parse(proxyString, Communicator);
            var prx2 = prx1.Clone();
            var prx3 = IServicePrx.Parse(prx2.ToString()!, Communicator);

            CheckGetHashCode(prx1, prx2);
            CheckGetHashCode(prx1, prx3);

            static void CheckGetHashCode(IServicePrx prx1, IServicePrx prx2)
            {
                Assert.AreEqual(prx1, prx2);
                Assert.AreEqual(prx1.GetHashCode(), prx2.GetHashCode());
                // The second attempt should hit the hash code cache
                Assert.AreEqual(prx1.GetHashCode(), prx2.GetHashCode());
            }
        }

        [Test]
        public async Task Proxy_InvokeAsync()
        {
            await using var communicator = new Communicator();
            await using var server = new Server
            {
                Invoker = communicator,
                Dispatcher = new GreeterService(),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            server.Listen();

            IGreeterServicePrx prx = server.CreateProxy<IGreeterServicePrx>("/");

            (ReadOnlyMemory<byte> responsePayload, Connection connection) = await prx.InvokeAsync(
                "SayHello",
                Payload.FromEmptyArgs(prx));

            Assert.DoesNotThrow(() => responsePayload.ToVoidReturnValue(prx, connection));
        }

        [TestCase("ice+tcp://host/test")]
        [TestCase("ice:test")]
        [TestCase("test:tcp -h host -p 10000")]
        [TestCase("test @ adapt")]
        public async Task Proxy_ParseWithOptionsAsync(string proxyString)
        {
            await using var communicator = new Communicator();
            var proxyOptions = new ProxyOptions() { Invoker = communicator };

            var proxy = IServicePrx.Factory.Parse(proxyString, proxyOptions);
            Assert.IsTrue(proxy.CacheConnection);
            Assert.IsFalse(proxy.IsOneway);
            Assert.AreEqual(proxy.InvocationTimeout, ProxyOptions.DefaultInvocationTimeout);
            CollectionAssert.IsEmpty(proxy.Context);

            proxyOptions.CacheConnection = false;
            proxyOptions.Context = new Dictionary<string, string>()
            {
                ["c1"] = "TEST1",
                ["c2"] = "TEST2"
            };
            proxyOptions.InvocationTimeout = TimeSpan.FromSeconds(1);
            proxyOptions.IsOneway = true;

            proxy = IServicePrx.Factory.Parse(proxyString, proxyOptions);
            proxyOptions.Context = ImmutableSortedDictionary<string, string>.Empty;

            Assert.IsFalse(proxy.CacheConnection);
            Assert.IsTrue(proxy.IsOneway);
            Assert.AreEqual(TimeSpan.FromSeconds(1), proxy.InvocationTimeout);
            Assert.AreEqual("TEST1", proxy.Context["c1"]);
            Assert.AreEqual("TEST2", proxy.Context["c2"]);
        }

        [Test]
        public async Task Proxy_UriOptions()
        {
            await using var communicator = new Communicator();
            string proxyString = "ice+tcp://localhost:10000/test";

            var prx = IServicePrx.Parse(proxyString, communicator);

            Assert.AreEqual("/test", prx.Path);
            prx = IServicePrx.Parse($"{proxyString}?cache-connection=false", communicator);
            Assert.IsFalse(prx.CacheConnection);

            prx = IServicePrx.Parse(
                    $"{proxyString}?context=c1=TEST1,c2=TEST&context=c2=TEST2,d%204=TEST%204,c3=TEST3",
                    communicator);

            Assert.AreEqual(4, prx.Context.Count);
            Assert.AreEqual("TEST1", prx.Context["c1"]);
            Assert.AreEqual("TEST2", prx.Context["c2"]);
            Assert.AreEqual("TEST3", prx.Context["c3"]);
            Assert.AreEqual("TEST 4", prx.Context["d 4"]);

            // This works because Context is a sorted dictionary
            Assert.AreEqual(prx.ToString(), $"{proxyString}?context=c1=TEST1,c2=TEST2,c3=TEST3,d%204=TEST%204");

            Assert.AreEqual(prx.InvocationTimeout, TimeSpan.FromSeconds(60));
            prx = IServicePrx.Parse($"{proxyString}?invocation-timeout=1s", communicator);
            Assert.AreEqual(prx.InvocationTimeout, TimeSpan.FromSeconds(1));

            string complicated = $"{proxyString}?invocation-timeout=10s&context=c%201=some%20value" +
                "&oneway=true&alt-endpoint=ice+ws://localhost?resource=/x/y&context=c5=v5";
            prx = IServicePrx.Parse(complicated, communicator);

            Endpoint altEndpoint = prx.AltEndpoints[0];
            Assert.AreEqual(1, prx.AltEndpoints.Count);
            Assert.AreEqual(Transport.WS, altEndpoint.Transport);
            Assert.AreEqual("/x/y", altEndpoint["resource"]);
            Assert.AreEqual(2, prx.Context.Count);
            Assert.AreEqual("some value", prx.Context["c 1"]);
            Assert.AreEqual("v5", prx.Context["c5"]);
            Assert.IsTrue(prx.IsOneway);
        }

        [TestCase("1.3")]
        [TestCase("2.1")]
        public async Task Proxy_NotSupportedEncoding(string encoding)
        {
            await using var communicator = new Communicator();
            var prx = IGreeterServicePrx.Parse("/test", communicator);
            prx.Encoding = Encoding.Parse(encoding);
            Assert.ThrowsAsync<NotSupportedException>(async () => await prx.IcePingAsync());
        }

        [TestCase("3")]
        [TestCase("4")]
        public async Task Proxy_NotSupportedProtocol(string protocol)
        {
            await using var communicator = new Communicator();
            var prx = IGreeterServicePrx.Parse($"ice+universal://localhost/test?transport=tcp&protocol={protocol}",
                                               communicator);
            Assert.ThrowsAsync<NotSupportedException>(async () => await prx.IcePingAsync());
        }

        [Test]
        public void Proxy_FactoryMethods()
        {
            Assert.AreEqual("/IceRpc.Service", IServicePrx.DefaultPath);
            IServicePrx service = IServicePrx.FromPath();
            Assert.AreEqual(IServicePrx.DefaultPath, service.Path);
            Assert.IsNull(service.Endpoint);

            service = IServicePrx.FromPath("/test");
            Assert.AreEqual("/test", service.Path);
            Assert.IsNull(service.Endpoint);

            Assert.AreEqual("/IceRpc.Tests.Api.GreeterService", IGreeterServicePrx.DefaultPath);
            IGreeterServicePrx greeter = IGreeterServicePrx.FromPath();
            Assert.AreEqual(IGreeterServicePrx.DefaultPath, greeter.Path);
            Assert.IsNull(greeter.Endpoint);

            greeter = IGreeterServicePrx.FromPath("/test");
            Assert.AreEqual("/test", greeter.Path);
            Assert.IsNull(greeter.Endpoint);

            var connection = new Connection { RemoteEndpoint = "ice+tcp://localhost:10000" };

            service = IServicePrx.FromConnection(connection);
            Assert.AreEqual(IServicePrx.DefaultPath, service.Path);
            Assert.AreEqual(connection, service.Connection);
            Assert.AreEqual(connection.RemoteEndpoint, service.Endpoint);

            greeter = IGreeterServicePrx.FromConnection(connection);
            Assert.AreEqual(IGreeterServicePrx.DefaultPath, greeter.Path);
            Assert.AreEqual(connection, greeter.Connection);
            Assert.AreEqual(connection.RemoteEndpoint, greeter.Endpoint);

            var server = new Server
            {
                Endpoint = "ice+tcp://127.0.0.1:10000",
                ProxyHost = "localhost"
            };

            service = IServicePrx.FromServer(server);
            Assert.AreEqual(IServicePrx.DefaultPath, service.Path);
            Assert.IsNull(service.Connection);
            Assert.AreEqual(server.ProxyEndpoint, service.Endpoint);

            greeter = IGreeterServicePrx.FromServer(server);
            Assert.AreEqual(IGreeterServicePrx.DefaultPath, greeter.Path);
            Assert.IsNull(greeter.Connection);
            Assert.AreEqual(server.ProxyEndpoint, greeter.Endpoint);

            connection = new Connection
            { 
                RemoteEndpoint = "ice+tcp://localhost:10000",
                Server = server,
            };

            service = IServicePrx.FromConnection(connection);
            Assert.AreEqual(IServicePrx.DefaultPath, service.Path);
            Assert.AreEqual(connection, service.Connection);
            Assert.IsNull(service.Endpoint);

            greeter = IGreeterServicePrx.FromConnection(connection);
            Assert.AreEqual(IGreeterServicePrx.DefaultPath, greeter.Path);
            Assert.AreEqual(connection, greeter.Connection);
            Assert.IsNull(greeter.Endpoint);
        }

        public class GreeterService : IGreeterService
        {
            public ValueTask SayHelloAsync(Dispatch dispatch, CancellationToken cancel) =>
                default;
        }
    }
}
