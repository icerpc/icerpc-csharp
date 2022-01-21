// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests.Api
{
    [Parallelizable(scope: ParallelScope.All)]
    [Timeout(30000)]
    public class EndpointTests
    {
        [Test]
        public void Endpoint_GetInit()
        {
            Endpoint endpoint = default;
            Assert.That(endpoint.Protocol, Is.Null);
            Assert.That(endpoint.Host, Is.Null);
            Assert.That(endpoint.Port, Is.EqualTo(0));
            Assert.That(endpoint.Params, Is.Null);

            endpoint = new Endpoint();
            Assert.That(endpoint.Protocol, Is.EqualTo(Protocol.IceRpc));
            Assert.That(endpoint.Host, Is.EqualTo("::0"));
            Assert.That(endpoint.Port, Is.EqualTo(Protocol.IceRpc.DefaultUriPort));
            Assert.That(endpoint.Params.Count, Is.EqualTo(0));

            endpoint = Endpoint.FromString("icerpc://host:10000?transport=foobar");
            Assert.That(endpoint.OriginalUri, Is.Not.Null);

            var endpoint2 = new Endpoint(endpoint.OriginalUri!);
            Assert.That(endpoint, Is.EqualTo(endpoint2));

            endpoint2 = endpoint with { Port = (ushort)(endpoint.Port + 1) };
            Assert.That(endpoint, Is.Not.EqualTo(endpoint2));
            Assert.That(endpoint2.OriginalUri, Is.Null);

            endpoint = endpoint with { Host = "localhost", Port = 1000 };
            endpoint = endpoint with { Host = "[::0]" };
            endpoint = endpoint with { Host = "::1" };

            endpoint = endpoint with { Params = endpoint.Params.Add("name%23[]", "value%25[]@!") };

            Assert.Catch<ArgumentException>(() => _ = endpoint with { Host = "" });
            Assert.Catch<ArgumentException>(() => _ = endpoint with { Host = "::1.2" });

            Assert.Catch<ArgumentException>(() => _ = endpoint with { Params = endpoint.Params.Add("", "value") });
            Assert.Catch<ArgumentException>(
                () => _ = endpoint with { Params = endpoint.Params.Add("alt-endpoint", "value") });
            Assert.Catch<ArgumentException>(() => _ = endpoint with { Params = endpoint.Params.Add("", "value") });
            Assert.Catch<ArgumentException>(() => _ = endpoint with { Params = endpoint.Params.Add(" a", "value") });
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
        [TestCase("icerpc://host:10000? =bar")]                        // parses ok even though not a valid name
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
