// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(30000)]
    public class EndpointTests
    {
        [TestCase("icerpc://host:10000")]
        [TestCase("icerpc://host:10000?transport=foobar")]
        [TestCase("icerpc://host")]
        [TestCase("icerpc://[::0]")]
        [TestCase("ice://[::0]?_foo=bar&tls=true")]
        [TestCase("icerpc://[::0]?tls=false&tls=true&foo=&b=")]
        [TestCase("icerpc://host:10000?tls=foo")]
        [TestCase("icerpc://host:10000?transport=coloc")]
        [TestCase("ice://localhost?transport=udp")]
        [TestCase("ice://host:10000")]
        public void Endpoint_Parse_ValidInput(string str)
        {
            var endpoint = Endpoint.FromString(str);
            var endpoint2 = Endpoint.FromString(endpoint.ToString());
            Assert.AreEqual(endpoint, endpoint2); // round trip works
        }

        [TestCase("icerpc://host:10000/category/name")]                // unexpected path
        [TestCase("icerpc://host:10000#fragment")]                     // unexpected fragment
        [TestCase("icerpc://host:10000?encoding=1.1")]                 // encoding is proxy-only
        [TestCase("icerpc://host:10000?alt-endpoint=host2")]           // alt-endpoint is proxy only
        [TestCase("icerpc://host:10000?tls")]                          // no = for tls parameter
        public void Endpoint_Parse_InvalidInput(string str) =>
            Assert.Throws<FormatException>(() => Endpoint.FromString(str));
    }
}
