using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ZeroC.Ice;

namespace IceRPC.Ice.Tests
{
    public class ProxyTestFixture : IAsyncLifetime
    {
        public Communicator Communicator { get; }
        public ProxyTestFixture() => Communicator = new Communicator();

        public Task InitializeAsync() => Task.CompletedTask;
        public async Task DisposeAsync() => await Communicator.DisposeAsync();
    }

    public class ProxyTest : IClassFixture<ProxyTestFixture>
    {
        private readonly ITestOutputHelper _output;
        private ProxyTestFixture _fixture;

        public ProxyTest(ITestOutputHelper output, ProxyTestFixture fixture)
        {
            _output = output;
            _fixture = fixture;
        }


        /// <summary>Test the parsing of valid proxies.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        /// <param name="protocol">The expected Protocol for the parsed proxy.</param>
        [Theory]
        [ClassData(typeof(ParsingValidProxyData))]
        public void ParsingValidProxy(string str, Protocol protocol)
        {
            var prx = IObjectPrx.Parse(str, _fixture.Communicator);
            Assert.Equal(protocol, prx.Protocol);
            var prx2 = IObjectPrx.Parse(prx.ToString()!, _fixture.Communicator);
            Assert.Equal(prx, prx2); // round-trip works
        }

        public class ParsingValidProxyData : TheoryData<string, Protocol>
        {
            public ParsingValidProxyData()
            {
                Add("ice -t:tcp -h localhost -p 10000", Protocol.Ice1);
                Add("ice+tcp:ssl -h localhost -p 10000", Protocol.Ice1);
                Add("ice+tcp://host.zeroc.com/identity#facet", Protocol.Ice2);
                Add("ice+tcp://host.zeroc.com:1000/category/name", Protocol.Ice2);
                Add("ice+tcp://host.zeroc.com:1000/loc0/loc1/category/name", Protocol.Ice2);
                Add("ice+tcp://host.zeroc.com/category/name%20with%20space", Protocol.Ice2);
                Add("ice+ws://host.zeroc.com//identity", Protocol.Ice2);
                Add("ice+ws://host.zeroc.com//identity?invocation-timeout=100ms", Protocol.Ice2);
                Add("ice+ws://host.zeroc.com//identity?invocation-timeout=1s", Protocol.Ice2);
                Add("ice+ws://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com", Protocol.Ice2);
                Add("ice+ws://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com:10000", Protocol.Ice2);
                Add("ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000,host2,host3,host4", Protocol.Ice2);
                Add("ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000&alt-endpoint=host2,host3&alt-endpoint=[::2]",
                    Protocol.Ice2);
                Add("ice:location//identity#facet", Protocol.Ice2);
                Add("ice:location//identity?relative=true#facet", Protocol.Ice2);
                Add("ice+tcp://host.zeroc.com//identity", Protocol.Ice2);
                // another syntax for empty port
                Add("ice+tcp://host.zeroc.com:/identity", Protocol.Ice2);
                Add("ice+universal://com.zeroc.ice/identity?transport=iaps&option=a,b%2Cb,c&option=d", Protocol.Ice2);
                Add("ice+universal://host.zeroc.com/identity?transport=100", Protocol.Ice2);
                // leading :: to make the address IPv6-like
                Add("ice+universal://[::ab:cd:ef:00]/identity?transport=bt", Protocol.Ice2);
                Add("ice+ws://host.zeroc.com/identity?resource=/foo%2Fbar?/xyz", Protocol.Ice2);
                Add("ice+universal://host.zeroc.com:10000/identity?transport=tcp", Protocol.Ice2);
                Add("ice+universal://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar", Protocol.Ice2);
                // a valid URI
                Add("ice:tcp -p 10000", Protocol.Ice2);
                // ice3 proxies
                Add("ice+universal://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar&protocol=3", (Protocol)3);
            }
        }

        /// <summary>Test the parsing of invalid proxies fails with <see cref="FormatException"/>.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        [Theory]
        [ClassData(typeof(ParsingInvalidProxyData))]
        public void ParsingInvalidProxy(string str)
        {
            Assert.Throws<FormatException>(() => IObjectPrx.Parse(str, _fixture.Communicator));
        }

        public class ParsingInvalidProxyData : TheoryData<string>
        {
            public ParsingInvalidProxyData()
            {
                Add("ice + tcp://host.zeroc.com:foo");    // missing host
                Add("ice+tcp:identity?protocol=invalid"); // invalid protocol
                Add("ice+universal://host.zeroc.com"); // missing transport
                Add("ice+universal://host.zeroc.com?transport=100&protocol=ice1"); // invalid protocol
                Add("ice://host:1000/identity"); // host not allowed
                Add("ice+universal:/identity"); // missing host
                Add("ice+tcp://host.zeroc.com/identity?protocol=3"); // unknown protocol (must use universal)
                Add("ice+ws://host.zeroc.com//identity?protocol=ice1"); // invalid protocol
                Add("ice+tcp://host.zeroc.com/identity?alt-endpoint=host2?protocol=ice2"); // protocol option in alt-endpoint
                Add("ice+tcp://host.zeroc.com/identity?foo=bar"); // unknown option
                Add("ice+tcp://host.zeroc.com/identity?invocation-timeout=0s"); // 0 is not a valid invocation timeout
                Add("ice:foo?relative=bad"); // bad value for relative
                Add("ice:foo?fixed=true"); // cannot create fixed proxy from URI

                Add("");
                Add("\"\"");
                Add("\"\" test"); // invalid trailing characters
                Add("test:"); // missing endpoint
                Add("id@adapter test");
                Add("id -f \"facet x");
                Add("id -f \'facet x");
                Add("test -f facet@test @test");
                Add("test -p 2.0");
                Add("test:tcp@location");
                Add("test: :tcp");
                Add("id:opaque -t 99 -v abcd -x abc"); // invalid x option
                Add("id:opaque"); // missing -t and -v
                Add("id:opaque -t 1 -t 1 -v abcd"); // repeated -t
                Add("id:opaque -t 1 -v abcd -v abcd");
                Add("id:opaque -v abcd");
                Add("id:opaque -t 1");
                Add("id:opaque -t -v abcd");
                Add("id:opaque -t 1 -v");
                Add("id:opaque -t x -v abcd");
                Add("id:opaque -t -1 -v abcd"); // -t must be >= 0
                Add("id:opaque -t 99 -v x?c"); // invalid char in v
                Add("id:opaque -t 99 -v xc"); // invalid length for base64 input
                Add("ice+tcp://0.0.0.0/identity#facet"); // Invalid Any IPv4 address in proxy endpoint
                Add("ice+tcp://[::0]/identity#facet"); // Invalid Any IPv6 address in proxy endpoint
                Add("identity:tcp -h 0.0.0.0"); // Invalid Any IPv4 address in proxy endpoint
                Add("identity:tcp -h [::0]"); // Invalid Any IPv6 address in proxy endpoint
            }
        }

        /// <summary>Test the parsing of invalid proxies fails with <see cref="FormatException"/>.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        [Theory]
        [ClassData(typeof(ParsingProxyIdentityAndLocationData))]
        public void ParsingProxyIdentityAndLocation(
            string str,
            string name,
            string category,
            IReadOnlyList<string> location)
        {
            var prx = IObjectPrx.Parse(str, _fixture.Communicator);
            Assert.Equal(name, prx.Identity.Name);
            Assert.Equal(category, prx.Identity.Category);
            Assert.Equal(location, prx.Location);
            Assert.Equal(0, prx.Facet.Length);
        }

        public class ParsingProxyIdentityAndLocationData :
            TheoryData<string, string, string, IReadOnlyList<string>>
        {
            public ParsingProxyIdentityAndLocationData()
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
            }

            private void Add(string str, string name) => Add(str, name, "");

            private void Add(string str, string name, string category) => Add(str, name, category, Array.Empty<string>());
        }
    }
}
