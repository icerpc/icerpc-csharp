// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading.Tasks;
using ZeroC.Ice;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    public class ProxyTests : ColocatedTest
    {
        /// <summary>Test the parsing of valid proxies.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        [TestCase("ice -t:tcp -h localhost -p 10000")]
        [TestCase("ice+tcp:ssl -h localhost -p 10000")]
        public void Proxy_Parse_ValidInputIce1Format(string str)
        {
            var prx = IServicePrx.Parse(str, Communicator);
            Assert.AreEqual(Protocol.Ice1, prx.Protocol);
            var prx2 = IServicePrx.Parse(prx.ToString()!, Communicator);
            Assert.AreEqual(prx, prx2); // round-trip works
        }

        [TestCase("ice+tcp://host.zeroc.com/identity#facet")]
        [TestCase("ice+tcp://host.zeroc.com:1000/category/name")]
        [TestCase("ice+tcp://host.zeroc.com:1000/loc0/loc1/category/name")]
        [TestCase("ice+tcp://host.zeroc.com/category/name%20with%20space")]
        [TestCase("ice+ws://host.zeroc.com//identity")]
        [TestCase("ice+ws://host.zeroc.com//identity?invocation-timeout=100ms")]
        [TestCase("ice+ws://host.zeroc.com//identity?invocation-timeout=1s")]
        [TestCase("ice+ws://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com")]
        [TestCase("ice+ws://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com:10000")]
        [TestCase("ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000,host2,host3,host4")]
        [TestCase("ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000&alt-endpoint=host2,host3&alt-endpoint=[::2]")]
        [TestCase("ice:location//identity#facet")]
        [TestCase("ice+tcp://host.zeroc.com//identity")]
        // another syntax for empty port
        [TestCase("ice+tcp://host.zeroc.com:/identity")]
        [TestCase("ice+universal://com.zeroc.ice/identity?transport=iaps&option=a,b%2Cb,c&option=d")]
        [TestCase("ice+universal://host.zeroc.com/identity?transport=100")]
        // leading :: to make the address IPv6-like
        [TestCase("ice+universal://[::ab:cd:ef:00]/identity?transport=bt")]
        [TestCase("ice+ws://host.zeroc.com/identity?resource=/foo%2Fbar?/xyz")]
        [TestCase("ice+universal://host.zeroc.com:10000/identity?transport=tcp")]
        [TestCase("ice+universal://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar")]
        [TestCase("ice+tcp://host:10000/test?source-address=::1")]
        // a valid URI
        [TestCase("ice:tcp -p 10000")]
        // ice3 proxies
        [TestCase("ice+universal://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar&protocol=3")]
        public void Proxy_Parse_ValidInputUriFormat(string str)
        {
            var prx = IServicePrx.Parse(str, Communicator);
            var prx2 = IServicePrx.Parse(prx.ToString()!, Communicator);
            Assert.AreEqual(prx, prx2); // round-trip works
        }

        /// <summary>Test that parsing an invalid proxies fails with <see cref="FormatException"/>.</summary>
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
        [TestCase("ice+tcp://0.0.0.0/identity#facet")] // Invalid Any IPv4 [TestCaseress in proxy endpoint
        [TestCase("ice+tcp://[::0]/identity#facet")] // Invalid Any IPv6 [TestCaseress in proxy endpoint
        [TestCase("identity:tcp -h 0.0.0.0")] // Invalid Any IPv4 [TestCaseress in proxy endpoint
        [TestCase("identity:tcp -h [::0]")] // Invalid Any IPv6 address in proxy endpoint
        public void Proxy_Parse_InvalidInput(string str)
        {
            Assert.Throws<FormatException>(() => IServicePrx.Parse(str, Communicator));
        }

        /// <summary>Test that the parsed proxy has the expected idenity and location</summary>
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
                Add("ice:test", "test");
                Add(" ice:test ", "test");
                Add(" ice:test", "test");
                Add("ice:test ", "test");
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

                Add("ice:category/test", "test", "category");

                Add("ice:loc0/loc1/category/test", "test", "category", new string[] { "loc0", "loc1" });
                Add("ice+tcp://host:10000/loc0/loc1//test?source-address=::1",
                    "test",
                    "",
                    new string[] { "loc0", "loc1" });

                Add("ice:server//test", "test", "", new string[] { "server" });
                Add("ice:server/category/test", "test", "category", new string[] { "server" });
                Add("ice:server:tcp/category/test", "test", "category", new string[] { "server:tcp" });
                Add("ice:server%3Atcp/category/test", "test", "category", new string[] { "server:tcp" });
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
    }
}
