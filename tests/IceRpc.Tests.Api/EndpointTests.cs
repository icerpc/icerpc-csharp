// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(30000)]
    public class EndpointTests
    {
        [TestCase("ice+tcp://host:10000")]
        [TestCase("ice+foobar://host:10000")]
        [TestCase("ice+tcp://host:10000?protocol=ice2")]
        [TestCase("ice+tcp://host:10000?protocol=3")]
        [TestCase("ice+tcp://host")]
        [TestCase("ice+tcp://[::0]")]
        [TestCase("ice+tcp://[::0]?_foo=bar&tls=true&protocol=5")]
        [TestCase("ice+tcp://[::0]?tls=false&tls=true&foo=&b=")]
        [TestCase("ice+tcp://host:10000?tls=foo")]
        [TestCase("ice+coloc://host:10000")]
        [TestCase("ice+xyz://host:10000")]
        [TestCase("tcp -h host -p 10000")]
        [TestCase("tcp -h \"::0\" -p 10000 --foo bar")]
        [TestCase("coloc -h host -p 10000")]
        [TestCase("abc -h x -p 5")]
        [TestCase("opaque -e 1.1 -t 1 -v CTEyNy4wLjAuMeouAAAQJwAAAA==")]
        [TestCase("opaque -t 2 -v CTEyNy4wLjAuMREnAAD/////AA==")]
        [TestCase("opaque -t 99 -e 1.1 -v abch")]
        [TestCase("tcp -h host -p 10000 -e 1.1")]  // -e is not reserved in ice1 strings
        [TestCase("ice+udp://localhost")]
        public void Endpoint_Parse_ValidInput(string str)
        {
            var endpoint = Endpoint.FromString(str);
            var endpoint2 = Endpoint.FromString(endpoint.ToString());
            Assert.AreEqual(endpoint, endpoint2); // round trip works
        }

        [TestCase("ice+tcp://host:10000/category/name")]                // unexpected path
        [TestCase("ice+tcp://host:10000?encoding=1.1")]                 // encoding is proxy-only
        [TestCase("ice+tcp://host:10000?protocol=ice4")]                // unknown protocol
        [TestCase("ice+tcp://host:10000?alt-endpoint=host2")]           // alt-endpoint is proxy only
        [TestCase("ice+tcp://host:10000?_tls")]                         // no = for parameter
        [TestCase("ice+tcp://host:10000?protocol=ice1")]                // can't use protocol ice1
        [TestCase("category/name:tcp -h host -p 10000")]                // unexpected path
        public void Endpoint_Parse_InvalidInput(string str) =>
            Assert.Throws<FormatException>(() => Endpoint.FromString(str));
    }
}
