// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(30000)]
    public class EndpointTests
    {
        [TestCase("icerpc+tcp://host:10000")]
        [TestCase("icerpc+foobar://host:10000")]
        [TestCase("icerpc+tcp://host")]
        [TestCase("icerpc+tcp://[::0]")]
        [TestCase("ice+tcp://[::0]?_foo=bar&tls=true")]
        [TestCase("icerpc+tcp://[::0]?tls=false&tls=true&foo=&b=")]
        [TestCase("icerpc+tcp://host:10000?tls=foo")]
        [TestCase("icerpc+coloc://host:10000")]
        [TestCase("icerpc+xyz://host:10000")]
        [TestCase("icerpc+udp://localhost")]
        [TestCase("ice+tcp://host:10000")]
        public void Endpoint_Parse_ValidInput(string str)
        {
            var endpoint = Endpoint.FromString(str);
            var endpoint2 = Endpoint.FromString(endpoint.ToString());
            Assert.AreEqual(endpoint, endpoint2); // round trip works
        }

        [TestCase("icerpc+tcp://host:10000/category/name")]                // unexpected path
        [TestCase("icerpc+tcp://host:10000#fragment")]                     // unexpected fragment
        [TestCase("icerpc+tcp://host:10000?encoding=1.1")]                 // encoding is proxy-only
        [TestCase("icerpc+tcp://host:10000?alt-endpoint=host2")]           // alt-endpoint is proxy only
        [TestCase("icerpc+tcp://host:10000?tls")]                          // no = for tls parameter
        public void Endpoint_Parse_InvalidInput(string str) =>
            Assert.Throws<FormatException>(() => Endpoint.FromString(str));
    }
}
