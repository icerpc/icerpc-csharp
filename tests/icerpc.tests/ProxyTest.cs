using System;
using System.Threading.Tasks;
using System.Collections;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Ice.Tests
{
    public class ProxyTest
    {
        public Communicator Communicator { get; }

        public ProxyTest() => Communicator = new Communicator();

        [OneTimeTearDown]
        public Task DisposeAsync() => Communicator.DisposeAsync().AsTask();

        /// <summary>Test the parsing of valid proxies.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        /// <param name="protocol">The expected Protocol for the parsed proxy.</param>
        [TestCaseSource(typeof(ParsingValidProxyTestCases))]
        public void ParsingValidProxy(string str, Protocol protocol)
        {
            var prx = IObjectPrx.Parse(str, Communicator);
            Assert.AreEqual(protocol, prx.Protocol);
            var prx2 = IObjectPrx.Parse(prx.ToString()!, Communicator);
            Assert.AreEqual(prx, prx2); // round-trip works
        }

        public class ParsingValidProxyTestCases : IEnumerable
        {
            public IEnumerator GetEnumerator()
            {
                yield return new object[]  { "ice -t:tcp -h localhost -p 10000", Protocol.Ice1 };
                yield return new object[]  { "ice+tcp:ssl -h localhost -p 10000", Protocol.Ice1 };
                yield return new object[]  { "ice+tcp://host.zeroc.com/identity#facet", Protocol.Ice2 };
                yield return new object[]  { "ice+tcp://host.zeroc.com:1000/category/name", Protocol.Ice2 };
                yield return new object[]  { "ice+tcp://host.zeroc.com:1000/loc0/loc1/category/name", Protocol.Ice2 };
                yield return new object[]  { "ice+tcp://host.zeroc.com/category/name%20with%20space", Protocol.Ice2 };
                yield return new object[]  { "ice+ws://host.zeroc.com//identity", Protocol.Ice2 };
                yield return new object[]
                { 
                    "ice+ws://host.zeroc.com//identity?invocation-timeout=100ms",
                    Protocol.Ice2
                };
                yield return new object[]
                {
                    "ice+ws://host.zeroc.com//identity?invocation-timeout=1s",
                    Protocol.Ice2
                };
                yield return new object[]
                { 
                    "ice+ws://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com",
                    Protocol.Ice2 
                };
                yield return new object[] 
                {
                    "ice+ws://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com:10000",
                    Protocol.Ice2
                };
                yield return new object[]
                {
                    "ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000,host2,host3,host4",
                    Protocol.Ice2
                };
                yield return new object[]
                {
                    "ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000&alt-endpoint=host2,host3&alt-endpoint=[::2]",
                    Protocol.Ice2
                };
                yield return new object[]  { "ice:location//identity#facet", Protocol.Ice2 };
                yield return new object[]  { "ice:location//identity?relative=true#facet", Protocol.Ice2 };
                yield return new object[]  { "ice+tcp://host.zeroc.com//identity", Protocol.Ice2 };
                // another syntax for empty port
                yield return new object[]  { "ice+tcp://host.zeroc.com:/identity", Protocol.Ice2 };
                yield return new object[]  
                {
                    "ice+universal://com.zeroc.ice/identity?transport=iaps&option=a,b%2Cb,c&option=d",
                    Protocol.Ice2
                };
                yield return new object[]
                {
                    "ice+universal://host.zeroc.com/identity?transport=100",
                    Protocol.Ice2
                };
                // leading :: to make the address IPv6-like
                yield return new object[]  { "ice+universal://[::ab:cd:ef:00]/identity?transport=bt", Protocol.Ice2 };
                yield return new object[]  { "ice+ws://host.zeroc.com/identity?resource=/foo%2Fbar?/xyz", Protocol.Ice2 };
                yield return new object[]  { "ice+universal://host.zeroc.com:10000/identity?transport=tcp", Protocol.Ice2 };
                yield return new object[]
                {
                    "ice+universal://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar",
                    Protocol.Ice2
                };
                yield return new object[]
                {
                    "ice+tcp://host:10000/test?source-address=::1",
                    Protocol.Ice2
                };
                // a valid URI
                yield return new object[]  { "ice:tcp -p 10000", Protocol.Ice2 };
                // ice3 proxies
                yield return new object[]
                {
                    "ice+universal://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar&protocol=3",
                    (Protocol)3
                };
            }
        }

        /// <summary>Test the parsing of invalid proxies fails with <see cref="FormatException"/>.</summary>
        /// <param name="str">The string to parse as a proxy.</param>
        [TestCase("ice + tcp://host.zeroc.com:foo")]    // missing host
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
        [TestCase("ice:foo?relative=bad")] // bad value for relative
        [TestCase("ice:foo?fixed=true")] // cannot create fixed proxy from URI
        [TestCase("")]
        [TestCase("\"\"")]
        [TestCase("\"\" test")] // invalid trailing characters
        [TestCase("test:")] // missing endpoint
        [TestCase("id@adapter test")]
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
        [TestCase("ice+tcp://0.0.0.0/identity#facet")] // Invalid Any IPv4 address in proxy endpoint
        [TestCase("ice+tcp://[::0]/identity#facet")] // Invalid Any IPv6 address in proxy endpoint
        [TestCase("identity:tcp -h 0.0.0.0")] // Invalid Any IPv4 address in proxy endpoint
        [TestCase("identity:tcp -h [::0]")] // Invalid Any IPv6 address in proxy endpoint
        public void ParsingInvalidProxy(string str)
        {
            Assert.Throws<FormatException>(() => IObjectPrx.Parse(str, Communicator));
        }
    }
}
