// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(30000)]
    public class EndpointTests
    {
        [TestCase("ice+tcp://host:10000")]
        [TestCase("ice+foobar://host:10000")]
        [TestCase("ice+tcp://host:10000?protocol=ice2")]
        [TestCase("ice+tcp://host:10000?protocol=ice3")]
        [TestCase("ice+tcp://host:10000?protocol=ice242")]
        [TestCase("ice+tcp://host")]
        [TestCase("ice+tcp://[::0]")]
        [TestCase("ice+tcp://[::0]?_foo=bar&tls=true&protocol=ice5")]
        [TestCase("ice+tcp://[::0]?tls=false&tls=true&foo=&b=")]
        [TestCase("ice+tcp://host:10000?tls=foo")]
        [TestCase("ice+coloc://host:10000")]
        [TestCase("ice+xyz://host:10000")]
        [TestCase("ice+udp://localhost")]
        [TestCase("ice+tcp://host:10000?protocol=ice1")]
        public void Endpoint_Parse_ValidInput(string str)
        {
            var endpoint = Endpoint.FromString(str);
            var endpoint2 = Endpoint.FromString(endpoint.ToString());
            Assert.AreEqual(endpoint, endpoint2); // round trip works
        }

        [TestCase("ice+tcp://host:10000/category/name")]                // unexpected path
        [TestCase("ice+tcp://host:10000?encoding=1.1")]                 // encoding is proxy-only
        [TestCase("ice+tcp://host:10000?protocol=4")]                   // invalid protocol
        [TestCase("ice+tcp://host:10000?protocol=ice2422")]             // invalid protocol
        [TestCase("ice+tcp://host:10000?protocol=icefoo")]              // invalid protocol
        [TestCase("ice+tcp://host:10000?alt-endpoint=host2")]           // alt-endpoint is proxy only
        [TestCase("ice+tcp://host:10000?_tls")]                         // no = for parameter
        [TestCase("category/name:tcp -h host -p 10000")]                // unexpected path
        public void Endpoint_Parse_InvalidInput(string str) =>
            Assert.Throws<FormatException>(() => Endpoint.FromString(str));
    }
}
