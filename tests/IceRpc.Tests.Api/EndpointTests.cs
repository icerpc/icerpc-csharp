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
        [TestCase("ice+tcp://host")]
        [TestCase("ice+tcp://[::0]")]
        [TestCase("ice+tcp://[::0]?tls=true")]
        [TestCase("ice+tcp://[::0]?tls=false")]
        [TestCase("ice+universal://host:10000?transport=tcp&protocol=ice2")]
        [TestCase("ice+universal://host:10000?transport=tcp&protocol=3")]
        [TestCase("ice+coloc://host:10000")]
        [TestCase("tcp -h host -p 10000")]
        [TestCase("tcp -h \"::0\" -p 10000")]
        [TestCase("coloc -h host -p 10000")]
        public void Endpoint_Parse_ValidInput(string str)
        {
            var endpoint = Endpoint.FromString(str);
            var endpoint2 = Endpoint.FromString(endpoint.ToString());
            Assert.AreEqual(endpoint, endpoint2); // round trip works
        }

        [TestCase("ice+tcp://host:10000")]
        [TestCase("ice+tcp://host")]
        [TestCase("ice+tcp://[::0]")]
        [TestCase("ice+tcp://[::0]?_foo=bar&tls=true&protocol=5")]
        [TestCase("ice+tcp://[::0]?tls=false&tls=true&foo=&b=")]
        [TestCase("ice+coloc://host:10000")]
        [TestCase("ice+xyz://host:10000")]
        [TestCase("tcp -h host -p 10000")]
        [TestCase("tcp -h \"::0\" -p 10000 --foo bar")]
        [TestCase("coloc -h host -p 10000")]
        [TestCase("abc -h x -p 5")]
        public void EndpointRecord_Parse_ValidInput(string str)
        {
            var endpoint = EndpointRecord.FromString(str);
            var endpoint2 = EndpointRecord.FromString(endpoint.ToString());
            Assert.AreEqual(endpoint, endpoint2); // round trip works
        }

        [TestCase("ice+tcp://host:10000/category/name")]
        [TestCase("ice+tcp://host:10000?protocol=ice2")]
        [TestCase("ice+tcp://host:10000?encoding=1.1")]
        [TestCase("ice+tcp://host:10000?alt-endpoint=host2")]
        [TestCase("ice+tcp://host:10000?tls")]
        [TestCase("ice+tcp://host:10000?tls=foo")]
        [TestCase("ice+coloc://host:10000?encoding=1.1")]
        [TestCase("ice+universal://host:10000?transport=tcp&protocol=ice1")]
        [TestCase("category/name:tcp -h host -p 10000")]
        [TestCase("tcp -h host -p 10000 -e 1.1")]
        [TestCase("ice+udp://localhost")]
        public void Endpoint_Parse_InvalidInput(string str) =>
            Assert.Throws<FormatException>(() => Endpoint.FromString(str));

        [TestCase("ice+universal://127.0.0.1:4062?transport=tcp", "ice+tcp://127.0.0.1")]
        [TestCase("ice+universal://127.0.0.1:4061?transport=tcp&option=a", "ice+tcp://127.0.0.1:4061")]
        // TODO: revisit
        // [TestCase("opaque -e 1.1 -t 1 -v CTEyNy4wLjAuMeouAAAQJwAAAA==", "tcp -h 127.0.0.1 -p 12010 -t 10000")]
        // [TestCase("opaque -e 1.1 -t 2 -v CTEyNy4wLjAuMREnAAD/////AA==", "ssl -h 127.0.0.1 -p 10001 -t -1")]
        [TestCase("opaque -t 99 -e 1.1 -v abch", "opaque -t 99 -e 1.1 -v abch")]
        public void Endpoint_Parse_UniversalOrOpaque(string original, string actual)
        {
            var endpoint = Endpoint.FromString(original);
            Assert.AreEqual(actual, endpoint.ToString());
        }
    }
}
