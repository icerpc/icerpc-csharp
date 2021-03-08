// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Interop.ZeroC.Ice;
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
        public async Task Proxy_Clone(string s)
        {
            await using var communicator = new Communicator();
            var prx = IServicePrx.Parse(s, communicator);

            Assert.IsFalse(prx.Clone(cacheConnection: false).CacheConnection);
            Assert.IsTrue(prx.Clone(cacheConnection: true).CacheConnection);

            var label = "my label";
            prx = prx.Clone(label: label);
            Assert.AreEqual(prx.Label, label);
            prx = prx.Clone(clearLabel: true);
            Assert.IsNull(prx.Label);

            prx = prx.Clone(context: new Dictionary<string, string>
            {
                { "key1", "value1" },
                { "key2", "value2" },
            });
            Assert.AreEqual(2, prx.Context.Count);
            Assert.AreEqual("value1", prx.Context["key1"]);
            Assert.AreEqual("value2", prx.Context["key2"]);

            Assert.AreEqual(prx.Clone(encoding: Encoding.V11).Encoding, Encoding.V11);
            Assert.AreEqual(prx.Clone(encoding: Encoding.V20).Encoding, Encoding.V20);

            if (prx.Protocol == Protocol.Ice1)
            {
                var prx2 = IServicePrx.Parse("test:tcp -h localhost -p 10001", communicator);
                Assert.AreEqual(prx.Clone(endpoints: prx2.Endpoints).Endpoints, prx2.Endpoints);
            }
            else
            {
                var prx2 = IServicePrx.Parse("ice+tcp://localhost:10001/test", communicator);
                Assert.AreEqual(prx.Clone(endpoints: prx2.Endpoints).Endpoints, prx2.Endpoints);
            }

            if (prx.Protocol == Protocol.Ice1)
            {
                Assert.AreEqual("facet", IServicePrx.Factory.Clone(prx, facet: "facet").Facet);
                Assert.AreEqual("id", prx.Clone(location: "id").Location);
            }

            var server = new Server(communicator, 
                                    new ServerOptions()
                                    {
                                        ColocationScope = ColocationScope.Communicator
                                    });
            prx = server.Add("test", new GreeterService(), IGreeterServicePrx.Factory);
            var connection = await prx.GetConnectionAsync();
            Assert.AreEqual(prx.Clone(fixedConnection: connection).GetCachedConnection(), connection);

            prx = IServicePrx.Parse(s, communicator);

            var intercetors = ImmutableList.Create<InvocationInterceptor>(
                (target, request, next, cancel) => throw new ArgumentException());
            Assert.AreEqual(intercetors, prx.Clone(invocationInterceptors: intercetors).InvocationInterceptors);

            Assert.AreEqual(prx.Clone(invocationTimeout: TimeSpan.FromMilliseconds(10)).InvocationTimeout,
                            TimeSpan.FromMilliseconds(10));

            Assert.IsFalse(prx.Clone(preferExistingConnection: false).PreferExistingConnection);
            Assert.IsTrue(prx.Clone(preferExistingConnection: true).PreferExistingConnection);

            Assert.IsFalse(prx.Clone(oneway: false).IsOneway);
            Assert.IsTrue(prx.Clone(oneway: true).IsOneway);

            if (prx.Protocol == Protocol.Ice1)
            {
                IServicePrx other = IServicePrx.Factory.Clone(prx, path: "test", facet: "facet");
                Assert.AreEqual("facet", other.Facet);
                Assert.AreEqual("test", other.Identity.Name);
                Assert.AreEqual("", other.Identity.Category);

                other = IServicePrx.Factory.Clone(other, path: "category/test");
                Assert.AreEqual("", other.Facet);
                Assert.AreEqual("test", other.Identity.Name);
                Assert.AreEqual("category", other.Identity.Category);

                other = IServicePrx.Factory.Clone(prx, path: "foo", facet: "facet1");
                Assert.AreEqual("facet1", other.Facet);
                Assert.AreEqual("foo", other.Identity.Name);
                Assert.AreEqual("", other.Identity.Category);
            }

            Assert.AreEqual(prx.Clone(preferNonSecure: NonSecure.Always).PreferNonSecure, NonSecure.Always);
            Assert.AreEqual(prx.Clone(preferNonSecure: NonSecure.Never).PreferNonSecure, NonSecure.Never);
        }

        [Test]
        public async Task Proxy_Clone_ArgumentException()
        {
            var prxIce1 = IServicePrx.Parse("hello:tcp -h localhost -p 10000", Communicator);
            Assert.AreEqual(Protocol.Ice1, prxIce1.Protocol);
            var prxIce2 = IServicePrx.Parse("ice+tcp://host.zeroc.com/hello", Communicator);
            Assert.AreEqual(Protocol.Ice2, prxIce2.Protocol);
            // Cannot set both label and clearLabel
            Assert.Throws<ArgumentException>(() => prxIce2.Clone(label: "foo", clearLabel: true));
            // Cannot set both locationService and clearLocationService
            Assert.Throws<ArgumentException>(() => prxIce1.Clone(locationService: new DummyLocationService(),
                                                                 clearLocationService: true));
            // locationService applies only to Ice1 proxies
            Assert.Throws<ArgumentException>(() => prxIce2.Clone(locationService: new DummyLocationService()));
            // clearLocationService applies only to Ice1 proxies
            Assert.Throws<ArgumentException>(() => prxIce2.Clone(clearLocationService: true));

            // Endpoints protocol must match the proxy protocol
            Assert.Throws<ArgumentException>(() => prxIce1.Clone(endpoints: prxIce2.Endpoints));
            Assert.Throws<ArgumentException>(() => prxIce2.Clone(endpoints: prxIce1.Endpoints));

            // cannot set both Endpoints and locationService
            Assert.Throws<ArgumentException>(() => IServicePrx.Parse("hello -t", Communicator).Clone(
                endpoints: prxIce1.Endpoints,
                locationService: new DummyLocationService()));

            // Zero is not a valid invocation timeout
            Assert.Throws<ArgumentException>(() => prxIce2.Clone(invocationTimeout: TimeSpan.Zero));

            await using var serverIce1 = new Server(Communicator, new()
            {
                Protocol = Protocol.Ice1,
                ColocationScope = ColocationScope.Communicator
            });
            var fixedPrxIce1 = serverIce1.Add("hello", new GreeterService(), IGreeterServicePrx.Factory);
            var connectionIce1 = await fixedPrxIce1.GetConnectionAsync();
            fixedPrxIce1 = fixedPrxIce1.Clone(fixedConnection: connectionIce1);
            Assert.IsTrue(fixedPrxIce1.IsFixed);
            Assert.AreEqual(Protocol.Ice1, fixedPrxIce1.Protocol);

            await using var serverIce2 = new Server(Communicator, new()
            {
                ColocationScope = ColocationScope.Communicator
            });
            var fixedPrxIce2 = serverIce2.Add("hello", new GreeterService(), IGreeterServicePrx.Factory);
            var connectionIce2 = await fixedPrxIce2.GetConnectionAsync();
            fixedPrxIce2 = fixedPrxIce2.Clone(fixedConnection: connectionIce2);
            Assert.IsTrue(fixedPrxIce2.IsFixed);
            Assert.AreEqual(Protocol.Ice2, fixedPrxIce2.Protocol);

            // Cannot change the endpoints of a fixed proxy
            Assert.Throws<ArgumentException>(() => fixedPrxIce2.Clone(endpoints: prxIce2.Endpoints));

            // Cannot change the cache connection setting of a fixed proxy
            Assert.Throws<ArgumentException>(() => fixedPrxIce2.Clone(cacheConnection: true));

            // Cannot change the label of a fixed proxy
            Assert.Throws<ArgumentException>(() => fixedPrxIce2.Clone(label: new object()));
            Assert.Throws<ArgumentException>(() => fixedPrxIce2.Clone(clearLabel: true));

            // Cannot change the location service of a fixed proxy
            Assert.Throws<ArgumentException>(() => fixedPrxIce1.Clone(locationService: new DummyLocationService()));
            Assert.Throws<ArgumentException>(() => fixedPrxIce1.Clone(clearLocationService: true));

            // Cannot change the prefer existing connection setting of a fixed proxy
            Assert.Throws<ArgumentException>(() => fixedPrxIce2.Clone(preferExistingConnection: true));

            // Cannot change the prefer non secure setting of a fixed proxy
            Assert.Throws<ArgumentException>(() => fixedPrxIce2.Clone(preferNonSecure: NonSecure.Always));
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
        [TestCase("ice+tcp://host:10000/test?source-address=::1", "/test")]
        [TestCase("ice+tcp://host:10000?source-address=::1", "/")]
        // a valid URI
        [TestCase("ice:tcp -p 10000")]
        // ice3 proxies
        [TestCase("ice+universal://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar&protocol=3")]
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
        [TestCase("ice+tcp://0.0.0.0/identity#facet")] // Invalid Any IPv4 in proxy endpoint
        [TestCase("ice+tcp://[::0]/identity#facet")] // Invalid Any IPv6 in proxy endpoint
        [TestCase("identity:tcp -h 0.0.0.0")] // Invalid Any IPv4 in proxy endpoint
        [TestCase("identity:tcp -h [::0]")] // Invalid Any IPv6 address in proxy endpoint
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
        [TestCaseSource(typeof(ParseProxyWithIdentityAndLocationTestCases))]
        public void Proxy_Parse_InputWithIdentityAndLocation(
            string str,
            string name,
            string category,
            IReadOnlyList<string> location)
        {
            var prx = IServicePrx.Parse(str, Communicator);
            Assert.AreEqual(name, prx.Identity.Name);
            Assert.AreEqual(category, prx.Identity.Category);
            Assert.AreEqual(location, prx.Location);
            Assert.AreEqual(0, prx.Facet.Length);
        }

        /// <summary>Test data for <see cref="Proxy_Parse_InputWithIdentityAndLocation"/>.</summary>
        public class ParseProxyWithIdentityAndLocationTestCases :
            TestData<string, string, string, IReadOnlyList<string>>
        {
            public ParseProxyWithIdentityAndLocationTestCases()
            {
                Add("test", "test");
                Add(" test ", "test");
                Add(" test", "test");
                Add("test ", "test");
                Add("'test -f facet'", "test -f facet");
                Add("\"test -f facet\"", "test -f facet");
                Add("\"test -f facet@test\"", "test -f facet@test");
                Add("\"test -f facet@test @test\"", "test -f facet@test @test");
                Add("test\\040test", "test test");
                Add("test\\40test", "test test");
                // Test some octal and hex corner cases.
                Add("test\\4test", "test\u0004test");
                Add("test\\04test", "test\u0004test");
                Add("test\\004test", "test\u0004test");
                Add("test\\1114test", "test\u00494test");
                Add("test\\b\\f\\n\\r\\t\\'\\\"\\\\test", "test\b\f\n\r\t\'\"\\test");
                Add("category/test", "test", "category");
            }

            private void Add(string str, string name) => Add(str, name, "");

            private void Add(string str, string name, string category) =>
                Add(str, name, category, Array.Empty<string>());
        }

        /// <summary>Test that the communicator default invocation interceptors are used when the
        /// proxy doesn't specify its own interceptors.</summary>
        [Test]
        public void Proxy_DefaultInvocationInterceptors()
        {
            var communicator = new Communicator
            {
                DefaultInvocationInterceptors = ImmutableList.Create<InvocationInterceptor>(
                    (target, request, next, cancel) => throw new NotImplementedException(),
                    (target, request, next, cancel) => throw new NotImplementedException())
            };

            var prx = IServicePrx.Parse("test", communicator);

            CollectionAssert.AreEqual(communicator.DefaultInvocationInterceptors, prx.InvocationInterceptors);
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

        /// <summary>Test that proxies that are equal produce the same hash code.</summary>
        [Test]
        public void Proxy_HashCode()
        {
            var prx1 = IServicePrx.Parse("hello:tcp -h localhost", Communicator);
            var prx2 = IServicePrx.Parse("hello:tcp -h localhost", Communicator);

            var prx3 = IServicePrx.Parse("bar:tcp -h 127.0.0.1 -p 10000", Communicator);

            CheckGetHashCode(prx1, prx2);

            CheckGetHashCode(prx1.Clone(cacheConnection: true), prx2.Clone(cacheConnection: true));

            CheckGetHashCode(prx1.Clone(endpoints: prx3.Endpoints), prx2.Clone(endpoints: prx3.Endpoints));

            CheckGetHashCode(prx1.Clone(invocationTimeout: TimeSpan.FromSeconds(1)),
                             prx2.Clone(invocationTimeout: TimeSpan.FromSeconds(1)));

            object label = new object();
            CheckGetHashCode(prx1.Clone(label: label), prx2.Clone(label: label));

            CheckGetHashCode(prx1.Clone(oneway: true), prx2.Clone(oneway: true));

            CheckGetHashCode(prx1.Clone(preferExistingConnection: true), prx2.Clone(preferExistingConnection: true));

            CheckGetHashCode(prx1.Clone(preferNonSecure: NonSecure.Always),
                             prx2.Clone(preferNonSecure: NonSecure.Always));

            static void CheckGetHashCode(IServicePrx prx1, IServicePrx prx2)
            {
                Assert.AreEqual(prx1, prx2);
                Assert.AreEqual(prx1.GetHashCode(), prx2.GetHashCode());
                // The second attempt should hit the hash code cache
                Assert.AreEqual(prx1.GetHashCode(), prx2.GetHashCode());
            }
        }

        [TestCase("ice+tcp://tcphost:10000/test?source-address=10.10.10.10" +
                  "&alt-endpoint=ice+universal://unihost:10000?transport=100$option=ABCD")]
        [TestCase("test -t:tcp -h tcphost -p 10000 -t 1200 -z " +
                  "--sourceAddress 10.10.10.10: udp -h udphost -p 10001 --interface eth0 --ttl 5 " +
                  "--sourceAddress 10.10.10.10:opaque -e 1.8 -t 100 -v ABCD")]
        public void Proxy_EndpointInformation(string prx)
        {
            var p1 = IServicePrx.Parse(prx, Communicator);

            IReadOnlyList<Endpoint> endps = p1.Endpoints;

            Endpoint tcpEndpoint = endps[0];
            Assert.AreEqual(tcpEndpoint.Transport, Transport.TCP);
            Assert.IsFalse(tcpEndpoint.IsAlwaysSecure);
            Assert.AreEqual(tcpEndpoint.Host, "tcphost");
            Assert.AreEqual(tcpEndpoint.Port, 10000);
            Assert.AreEqual(tcpEndpoint["source-address"], "10.10.10.10");

            if (p1.Protocol == Protocol.Ice1)
            {
                Assert.AreEqual(tcpEndpoint["timeout"], "1200");
                Assert.AreEqual(tcpEndpoint["compress"], "true");
            }
            Assert.IsFalse(tcpEndpoint.IsDatagram);

            if (p1.Protocol == Protocol.Ice1)
            {
                Endpoint udpEndpoint = endps[1];
                Assert.AreEqual("udphost", udpEndpoint.Host);
                Assert.AreEqual(10001, udpEndpoint.Port);
                Assert.AreEqual("eth0", udpEndpoint["interface"]);
                Assert.AreEqual("5", udpEndpoint["ttl"]);
                Assert.AreEqual("10.10.10.10", udpEndpoint["source-address"]);
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
        public async Task Proxy_Fixed(Protocol protocol, string expected)
        {
            await using var communicator = new Communicator();
            await using var server = new Server(
                communicator, 
                new ServerOptions() { Protocol = protocol, ColocationScope = ColocationScope.Communicator });
            var prx = server.Add("greeter", new GreeterService(), IGreeterServicePrx.Factory);
            Connection connection = await prx.GetConnectionAsync();
            Assert.AreEqual(expected, IServicePrx.Factory.Create(connection, "fixed").ToString());
        }

        [Test]
        public async Task Proxy_PropertyAsProxy()
        {
            string propertyPrefix = "Foo.Proxy";
            string proxyString = "test:tcp -h localhost -p 10000";

            await using var communicator = new Communicator();

            communicator.SetProperty(propertyPrefix, proxyString);
            var prx = communicator.GetPropertyAsProxy(propertyPrefix, IServicePrx.Factory)!;
            Assert.AreEqual(prx.Path, "/test");

            Assert.IsTrue(prx.CacheConnection);
            communicator.SetProperty($"{propertyPrefix}.CacheConnection", "0");
            prx = communicator.GetPropertyAsProxy(propertyPrefix, IServicePrx.Factory)!;
            communicator.RemoveProperty($"{propertyPrefix}.CacheConnection");
            Assert.IsFalse(prx.CacheConnection);

            Assert.IsFalse(prx.Context.ContainsKey("c1"));
            communicator.SetProperty($"{propertyPrefix}.Context.c1", "TEST1");
            prx = communicator.GetPropertyAsProxy(propertyPrefix, IServicePrx.Factory)!;
            Assert.AreEqual(prx.Context["c1"], "TEST1");

            Assert.IsFalse(prx.Context.ContainsKey("c2"));
            communicator.SetProperty($"{propertyPrefix}.Context.c2", "TEST2");
            prx = communicator.GetPropertyAsProxy(propertyPrefix, IServicePrx.Factory)!;
            Assert.AreEqual(prx.Context["c2"], "TEST2");

            communicator.SetProperty($"{propertyPrefix}.Context.c1", "");
            communicator.SetProperty($"{propertyPrefix}.Context.c2", "");

            Assert.AreEqual(prx.InvocationTimeout, TimeSpan.FromSeconds(60));

            communicator.SetProperty($"{propertyPrefix}.InvocationTimeout", "1s");
            prx = communicator.GetPropertyAsProxy(propertyPrefix, IServicePrx.Factory)!;
            communicator.SetProperty($"{propertyPrefix}.InvocationTimeout", "");
            Assert.AreEqual(prx.InvocationTimeout, TimeSpan.FromSeconds(1));

            Assert.AreEqual(prx.PreferNonSecure, communicator.DefaultPreferNonSecure);
            communicator.SetProperty($"{propertyPrefix}.PreferNonSecure", "SameHost");
            prx = communicator.GetPropertyAsProxy(propertyPrefix, IServicePrx.Factory)!;
            communicator.RemoveProperty($"{propertyPrefix}.PreferNonSecure");
            Assert.AreNotEqual(prx.PreferNonSecure, communicator.DefaultPreferNonSecure);
        }

        [Test]
        public async Task Proxy_ToProperty()
        {
            await using var communicator = new Communicator();
            var prx = IServicePrx.Parse("test -t -e 1.1:tcp -h 127.0.0.1 -p 12010 -t 1000", communicator).Clone(
                cacheConnection: true,
                preferExistingConnection: true,
                preferNonSecure: NonSecure.Never,
                invocationTimeout: TimeSpan.FromSeconds(10));

            Dictionary<string, string> proxyProps = prx.ToProperty("Test");
            Assert.AreEqual(proxyProps.Count, 4);
            Assert.AreEqual("test -t -e 1.1:tcp -h 127.0.0.1 -p 12010 -t 1000", proxyProps["Test"]);

            Assert.AreEqual("10s", proxyProps["Test.InvocationTimeout"]);
            Assert.AreEqual("Never", proxyProps["Test.PreferNonSecure"]);

            ILocatorPrx locator = ILocatorPrx.Parse("locator", communicator).Clone(
                cacheConnection: false,
                preferExistingConnection: false,
                preferNonSecure: NonSecure.Always);

            // TODO: LocationService should reject indirect locators.
            ILocationService locationService = new LocationService(locator);
            prx = prx.Clone(locationService: locationService);

            proxyProps = prx.ToProperty("Test");

            Assert.AreEqual(4, proxyProps.Count);
            Assert.AreEqual("test -t -e 1.1", proxyProps["Test"]);
            Assert.AreEqual("10s", proxyProps["Test.InvocationTimeout"]);
            Assert.AreEqual("Never", proxyProps["Test.PreferNonSecure"]);
            Assert.AreEqual("true", proxyProps["Test.PreferExistingConnection"]);
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

            Assert.AreEqual(prx.Context.Count, 4);
            Assert.AreEqual(prx.Context["c1"], "TEST1");
            Assert.AreEqual(prx.Context["c2"], "TEST2");
            Assert.AreEqual(prx.Context["c3"], "TEST3");
            Assert.AreEqual(prx.Context["d 4"], "TEST 4");

            // This works because Context is a sorted dictionary
            Assert.AreEqual(prx.ToString(), $"{proxyString}?context=c1=TEST1,c2=TEST2,c3=TEST3,d%204=TEST%204");

            Assert.AreEqual(prx.InvocationTimeout, TimeSpan.FromSeconds(60));
            prx = IServicePrx.Parse($"{proxyString}?invocation-timeout=1s", communicator);
            Assert.AreEqual(prx.InvocationTimeout, TimeSpan.FromSeconds(1));

            Assert.AreEqual(prx.PreferNonSecure, communicator.DefaultPreferNonSecure);
            prx = IServicePrx.Parse($"{proxyString}?prefer-non-secure=SameHost", communicator);
            Assert.AreNotEqual(prx.PreferNonSecure, communicator.DefaultPreferNonSecure);

            string complicated = $"{proxyString}?invocation-timeout=10s&context=c%201=some%20value" +
                    "&alt-endpoint=ice+ws://localhost?resource=/x/y$source-address=[::1]&context=c5=v5";
            prx = IServicePrx.Parse(complicated, communicator);

            Assert.AreEqual(prx.Endpoints.Count, 2);
            Assert.AreEqual(prx.Endpoints[1].Transport, Transport.WS);
            Assert.AreEqual(prx.Endpoints[1]["resource"], "/x/y");
            Assert.AreEqual(prx.Endpoints[1]["source-address"], "::1");
            Assert.AreEqual(prx.Context.Count, 2);
            Assert.AreEqual(prx.Context["c 1"], "some value");
            Assert.AreEqual(prx.Context["c5"], "v5");
        }

        internal class DummyLocationService : ILocationService
        {
            public ValueTask<(IReadOnlyList<Endpoint> Endpoints, TimeSpan EndpointsAge)> ResolveLocationAsync(
                string location,
                TimeSpan endpointsMaxAge,
                CancellationToken cancel) => throw new NotImplementedException();

            public ValueTask<(IReadOnlyList<Endpoint> Endpoints, TimeSpan EndpointsAge)> ResolveWellKnownProxyAsync(
                Identity identity,
                TimeSpan endpointsMaxAge,
                CancellationToken cancel) => throw new NotImplementedException();
        }

        public class GreeterService : IAsyncGreeterService
        {
            public ValueTask SayHelloAsync(Current current, CancellationToken cancel) =>
                throw new NotImplementedException();
        }
    }
}
