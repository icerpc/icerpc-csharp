// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    public class ProxyTests : ColocatedTest
    {
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
                prx.Endpoints = prx2.Endpoints;
                Assert.AreEqual(prx.Endpoints, prx2.Endpoints);
            }
            else
            {
                var prx2 = IGreeterServicePrx.Parse("ice+tcp://localhost:10001/test", communicator);
                prx.Endpoints = prx2.Endpoints;
                Assert.AreEqual(prx.Endpoints, prx2.Endpoints);
            }

            if (prx.Protocol == Protocol.Ice1)
            {
                Assert.AreEqual("facet", prx.WithFacet<IGreeterServicePrx>("facet").GetFacet());
            }

            var server = new Server(communicator,
                                    new ServerOptions()
                                    {
                                        ColocationScope = ColocationScope.Communicator
                                    });
            prx = server.Add("test", new GreeterService(), IGreeterServicePrx.Factory);
            var connection = await prx.GetConnectionAsync();

            prx = IGreeterServicePrx.Parse(s, communicator);

            var interceptors = ImmutableList.Create<InvocationInterceptor>(
                (target, request, next, cancel) => throw new ArgumentException());

            prx.InvocationInterceptors = interceptors;
            Assert.AreEqual(interceptors, prx.InvocationInterceptors);

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
                    prx.WithPath<IGreeterServicePrx>("test").WithFacet<IGreeterServicePrx>("facet");

                Assert.AreEqual("facet", other.GetFacet());
                Assert.AreEqual("test", other.GetIdentity().Name);
                Assert.AreEqual("", other.GetIdentity().Category);

                other = other.WithPath<IGreeterServicePrx>("category/test");
                Assert.AreEqual("facet", other.GetFacet());
                Assert.AreEqual("test", other.GetIdentity().Name);
                Assert.AreEqual("category", other.GetIdentity().Category);

                other = prx.WithPath<IGreeterServicePrx>("foo").WithFacet<IGreeterServicePrx>("facet1");
                Assert.AreEqual("facet1", other.GetFacet());
                Assert.AreEqual("foo", other.GetIdentity().Name);
                Assert.AreEqual("", other.GetIdentity().Category);
            }

            prx.NonSecure = NonSecure.Always;
            Assert.AreEqual(NonSecure.Always, prx.NonSecure);
            prx.NonSecure = NonSecure.Never;
            Assert.AreEqual(NonSecure.Never, prx.NonSecure);
        }

        [Test]
        public void Proxy_SetProperty_ArgumentException()
        {
            var prxIce1 = IServicePrx.Parse("hello:tcp -h localhost -p 10000", Communicator);
            Assert.AreEqual(Protocol.Ice1, prxIce1.Protocol);
            var prxIce2 = IServicePrx.Parse("ice+tcp://host.zeroc.com/hello", Communicator);
            Assert.AreEqual(Protocol.Ice2, prxIce2.Protocol);

            // Endpoints protocol must match the proxy protocol
            Assert.Throws<ArgumentException>(() => prxIce1.Endpoints = prxIce2.Endpoints);
            Assert.Throws<ArgumentException>(() => prxIce2.Endpoints = prxIce1.Endpoints);

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
        // a valid URI
        [TestCase("ice:tcp -p 10000")]
        // ice3 proxies
        [TestCase("ice+universal://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar&protocol=3")]
        [TestCase("ice+tcp://0.0.0.0/identity#facet")] // Any IPv4 in proxy endpoint (unusable but parses ok)
        [TestCase("ice+tcp://[::0]/identity#facet")] // Any IPv6 in proxy endpoint (unusable but parses ok)
        [TestCase("identity:tcp -h 0.0.0.0")] // Any IPv4 in proxy endpoint (unusable but parses ok)
        [TestCase("identity:tcp -h \"::0\"")] // Any IPv6 address in proxy endpoint (unusable but parses ok)
        public void Proxy_Parse_ValidInputUriFormat(string str, string? path = null)
        {
            var prx = IServicePrx.Parse(str, Communicator);
            var prx2 = IServicePrx.Parse(prx.ToString()!, Communicator);
            Assert.AreEqual(prx, prx2); // round-trip works

            if (path != null)
            {
                Assert.AreEqual(path, prx.Path);
            }
        }

        /// <summary>Tests that parsing an invalid proxies fails with <see cref="FormatException"/>.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        [TestCase("ice + tcp://host.zeroc.com:foo")] // missing host
        [TestCase("ice+tcp:identity?protocol=invalid")] // invalid protocol
        [TestCase("ice+universal://host.zeroc.com")] // missing transport
        [TestCase("ice+universal://host.zeroc.com?transport=100&protocol=ice1")] // invalid protocol
        [TestCase("ice://host:1000/identity")] // host not allowed
        [TestCase("ice+universal:/identity")] // missing host
        [TestCase("ice+tcp://host.zeroc.com/identity?protocol=3")] // unknown protocol (must use universal)
        [TestCase("ice+ws://host.zeroc.com//identity?protocol=ice1")] // invalid protocol
        [TestCase("ice+tcp://host.zeroc.com/identity?alt-endpoint=host2?protocol=ice2")] // protocol option in alt-endpoint
        [TestCase("ice+tcp://host.zeroc.com/identity?foo=bar")] // unknown option
        [TestCase("ice+tcp://host.zeroc.com/identity?invocation-timeout=0s")] // 0 is not a valid invocation timeout
        [TestCase("ice:foo?fixed=true")] // cannot create fixed proxy from URI
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

            IReadOnlyList<Endpoint> endps = p1.Endpoints;

            Endpoint tcpEndpoint = endps[0];
            Assert.AreEqual(Transport.TCP, tcpEndpoint.Transport);
            Assert.IsFalse(tcpEndpoint.IsAlwaysSecure);
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
                Endpoint udpEndpoint = endps[1];
                Assert.AreEqual("239.255.1.1", udpEndpoint.Host);
                Assert.AreEqual(10001, udpEndpoint.Port);
                Assert.AreEqual("eth0", udpEndpoint["interface"]);
                Assert.AreEqual("5", udpEndpoint["ttl"]);
                Assert.AreEqual(null, udpEndpoint["timeout"]);
                Assert.AreEqual(null, udpEndpoint["compress"]);
                Assert.IsFalse(udpEndpoint.IsAlwaysSecure);
                Assert.IsTrue(udpEndpoint.IsDatagram);
                Assert.AreEqual(Transport.UDP, udpEndpoint.Transport);

                Endpoint opaqueEndpoint = endps[2];
                Assert.AreEqual("ABCD", opaqueEndpoint["value"]);
                Assert.AreEqual("1.8", opaqueEndpoint["value-encoding"]);
            }
            else
            {
                Endpoint universalEndpoint = endps[1];
                Assert.AreEqual((Transport)100, universalEndpoint.Transport);
                Assert.AreEqual("ABCD", universalEndpoint["option"]);
            }
        }

        [TestCase(Protocol.Ice1, "fixed -t -e 1.1")]
        [TestCase(Protocol.Ice2, "ice:/fixed?fixed=true")]
        public async Task Proxy_Fixed(Protocol protocol, string _)
        {
            await using var communicator = new Communicator();
            await using var server = new Server(
                communicator,
                new ServerOptions() { Protocol = protocol, ColocationScope = ColocationScope.Communicator });
            var prx = server.Add("greeter", new GreeterService(), IGreeterServicePrx.Factory);

            // TODO: this does not work. A fixed proxy must be bound to a non-coloc connection.

            // Connection connection = await prx.GetConnectionAsync();

            // prx.FixedConnection = connection;
            // Assert.AreEqual(expected, prx.WithPath<IGreeterServicePrx>("fixed").ToString());
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

        [TestCase("ice+tcp://host/test")]
        [TestCase("ice:test")]
        [TestCase("test:tcp -h host -p 10000")]
        [TestCase("test @ adapt")]
        public async Task Proxy_ParseWithOptionsAsync(string proxyString)
        {
            await using var communicator = new Communicator();
            var proxyOptions = new ProxyOptions() { Communicator = communicator };

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
        public async Task Proxy_PropertyAsProxy()
        {
            string propertyPrefix = "Foo.Proxy";
            string proxyString = "test:tcp -h localhost -p 10000";

            await using var communicator = new Communicator();

            communicator.SetProperty(propertyPrefix, proxyString);
            var prx = communicator.GetPropertyAsProxy(propertyPrefix, IServicePrx.Factory)!;
            Assert.AreEqual("/test", prx.Path);
        }

        [Test]
        public async Task Proxy_ToProperty()
        {
            await using var communicator = new Communicator();
            var prx = IServicePrx.Parse("test -t -e 1.1:tcp -h 127.0.0.1 -p 12010 -t 1000", communicator);
            prx.CacheConnection = true;
            prx.PreferExistingConnection = false;
            prx.NonSecure = NonSecure.Never;
            prx.InvocationTimeout = TimeSpan.FromSeconds(10);

            Dictionary<string, string> proxyProps = prx.ToProperty("Test");
            Assert.AreEqual(4, proxyProps.Count);
            Assert.AreEqual("test -t -e 1.1:tcp -h 127.0.0.1 -p 12010 -t 1000", proxyProps["Test"]);

            Assert.AreEqual("10s", proxyProps["Test.InvocationTimeout"]);
            Assert.AreEqual("Never", proxyProps["Test.NonSecure"]);

            ILocatorPrx locator = ILocatorPrx.Parse("locator", communicator);
            locator.CacheConnection = false;
            locator.PreferExistingConnection = false;
            locator.NonSecure = NonSecure.Always;

            // TODO: LocatorClient should reject indirect locators.
            ILocationResolver locationResolver = new LocatorClient(locator);
            prx.LocationResolver = locationResolver;

            proxyProps = prx.ToProperty("Test");

            Assert.AreEqual(4, proxyProps.Count);
            Assert.AreEqual("test -t -e 1.1:tcp -h 127.0.0.1 -p 12010 -t 1000", proxyProps["Test"]);
            Assert.AreEqual("10s", proxyProps["Test.InvocationTimeout"]);
            Assert.AreEqual("Never", proxyProps["Test.NonSecure"]);
            Assert.AreEqual("false", proxyProps["Test.PreferExistingConnection"]);
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

            Assert.AreEqual(2, prx.Endpoints.Count);
            Assert.AreEqual(Transport.WS, prx.Endpoints[1].Transport);
            Assert.AreEqual("/x/y", prx.Endpoints[1]["resource"]);
            Assert.AreEqual(2, prx.Context.Count);
            Assert.AreEqual("some value", prx.Context["c 1"]);
            Assert.AreEqual("v5", prx.Context["c5"]);
            Assert.IsTrue(prx.IsOneway);
        }

        public class GreeterService : IAsyncGreeterService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) =>
                throw new NotImplementedException();
        }
    }
}
