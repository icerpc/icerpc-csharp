// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(30000)]
    public class EndpointTests
    {
        [TestCase("icerpc://host:10000?transport=foobar")]
        [TestCase("ice://host")]
        public void Endpoint_GetInit(string str)
        {
            var endpoint = Endpoint.FromString(str);

            var endpoint2 = new Endpoint(endpoint.OriginalUri!);
            Assert.That(endpoint, Is.EqualTo(endpoint2));

            endpoint2 = endpoint with { Port = (ushort)(endpoint.Port + 1) };
            Assert.That(endpoint, Is.Not.EqualTo(endpoint2));

            endpoint = endpoint.Protocol == Protocol.IceRpc ?
                endpoint with { Protocol = Protocol.Ice } : endpoint with { Protocol = Protocol.IceRpc };
            Assert.That(endpoint.OriginalUri, Is.Null);

            endpoint = endpoint with { Host = "localhost", Port = 1000 };
            endpoint = endpoint with { Host = "[::0]" };
            endpoint = endpoint with { Host = "::1" };

            endpoint = endpoint with { Params = endpoint.Params.Add("name", "value") };

            Assert.Catch<ArgumentException>(() => _ = endpoint with { Protocol = Protocol.Relative });
            Assert.Catch<ArgumentException>(() => _ = endpoint with { Protocol = Protocol.FromString("foo") });

            Assert.Catch<ArgumentException>(() => _ = endpoint with { Host = "" });
            Assert.Catch<ArgumentException>(() => _ = endpoint with { Host = "::1.2" });

            Assert.Catch<ArgumentException>(() => _ = endpoint with { Params = endpoint.Params.Add("", "value") });
            Assert.Catch<ArgumentException>(
                () => _ = endpoint with { Params = endpoint.Params.Add("alt-endpoint", "value") });
            Assert.Catch<ArgumentException>(() => _ = endpoint with { Params = endpoint.Params.Add("a=b", "value") });
            Assert.Catch<ArgumentException>(() => _ = endpoint with { Params = endpoint.Params.Add("a.b", "value") });
            Assert.Catch<ArgumentException>(() => _ = endpoint with { Params = endpoint.Params.Add("x", "a#b") });
            Assert.Catch<ArgumentException>(() => _ = endpoint with { Params = endpoint.Params.Add("x", "a&b") });
        }

        [TestCase("icerpc://host:10000")]
        [TestCase("icerpc://host:10000?transport=foobar")]
        [TestCase("icerpc://host")]
        [TestCase("icerpc://[::0]")]
        [TestCase("ice://[::0]?foo=bar&tls=true")]
        [TestCase("icerpc://[::0]?tls=false&tls=true&foo=&b=")]
        [TestCase("icerpc://host:10000?tls=foo")]
        [TestCase("icerpc://host:10000?transport=coloc")]
        [TestCase("ice://localhost?transport=udp")]
        [TestCase("ice://host:10000")]
        [TestCase("icerpc://host:10000?tls")]
        [TestCase("icerpc://host:10000?tls&adapter-id=ok")]
        [TestCase("IceRpc://host:10000")]
        public void Endpoint_Parse_ValidInput(string str)
        {
            var endpoint = Endpoint.FromString(str);
            var endpoint2 = Endpoint.FromString(endpoint.ToString());
            Assert.That(endpoint, Is.EqualTo(endpoint2)); // round trip works
        }

        [TestCase("icerpc://host:10000/category/name")]                // unexpected path
        [TestCase("icerpc://host:10000#fragment")]                     // unexpected fragment
        [TestCase("icerpc://host:10000?alt-endpoint=host2")]           // alt-endpoint is proxy only
        [TestCase("icerpc://host:10000?=bar")]                         // empty param name
        [TestCase("icerpc:///foo")]                                    // path, empty authority
        [TestCase("icerpc:///")]                                       // empty authority
        [TestCase("icerpc://")]                                        // empty authority
        [TestCase("icerpc:/foo")]                                      // no authority
        [TestCase("icerpc:")]                                          // no authority
        [TestCase("foo://host:10000")]                                 // protocol not supported
        [TestCase("icerpc://user:password@host:10000")]                // bad user-info
        [TestCase("icerpc://host:70000")]                              // bad port
        [TestCase("icerpc://host:10_000")]                             // bad port
        [TestCase("icerpc://host:10000? =bar")]                        // bad param name
        [TestCase("icerpc://host:10000?a.b=bar")]                      // bad param name
        [TestCase("icerpc://host:10000?a_b=bar")]                      // bad param name
        public void Endpoint_Parse_InvalidInput(string str)
        {
            Assert.Catch<FormatException>(() => Endpoint.FromString(str));

            try
            {
                _ = new Endpoint(new Uri(str));
                Assert.Fail("expected exception");
            }
            catch (ArgumentException)
            {
            }
            catch (FormatException)
            {
            }
        }
    }
}
