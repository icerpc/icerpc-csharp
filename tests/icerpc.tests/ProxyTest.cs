using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using ZeroC.Ice;

namespace IceRPC.Ice.Tests
{
    public class ProxyTest
    {
        [Theory]
        [ClassData(typeof(TestProxyParsingData))]
        public async Task TestProxyParsingAsync(string str)
        {
            await using var communicator = new Communicator();
            var prx = IObjectPrx.Parse(str, communicator);
            Assert.Equal(Protocol.Ice2, prx.Protocol);
            // output.WriteLine($"{str} = {prx}");
            var prx2 = IObjectPrx.Parse(prx.ToString()!, communicator);
            Assert.Equal(prx, prx2); // round-trip works
        }
        public class TestProxyParsingData : IEnumerable<object[]>
        {
            public IEnumerator<object[]> GetEnumerator()
            {
                yield return new object[] { "ice+tcp://host.zeroc.com/identity#facet" };
                yield return new object[] { "ice+tcp://host.zeroc.com:1000/category/name" };
                yield return new object[] { "ice+tcp://host.zeroc.com:1000/loc0/loc1/category/name" };
                yield return new object[] { "ice+tcp://host.zeroc.com/category/name%20with%20space" };
                yield return new object[] { "ice+ws://host.zeroc.com//identity" };
                yield return new object[] { "ice+ws://host.zeroc.com//identity?invocation-timeout=100ms" };
                yield return new object[] { "ice+ws://host.zeroc.com//identity?invocation-timeout=1s" };
                yield return new object[] { "ice+ws://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com" };
                yield return new object[] { "ice+ws://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com:10000" };
                yield return new object[] { "ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000,host2,host3,host4" };
                yield return new object[] { "ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000&alt-endpoint=host2,host3&alt-endpoint=[::2]" };
                yield return new object[] { "ice:location//identity#facet" };
                yield return new object[] { "ice:location//identity?relative=true#facet" };
                yield return new object[] { "ice+tcp://host.zeroc.com//identity" };
                yield return new object[] { "ice+tcp://host.zeroc.com:/identity" }; // another syntax for empty port
                yield return new object[] { "ice+universal://com.zeroc.ice/identity?transport=iaps&option=a,b%2Cb,c&option=d" };
                yield return new object[] { "ice+universal://host.zeroc.com/identity?transport=100" };
                yield return new object[] { "ice+universal://[::ab:cd:ef:00]/identity?transport=bt" }; // leading :: to make the address IPv6-like
                yield return new object[] { "ice+ws://host.zeroc.com/identity?resource=/foo%2Fbar?/xyz" };
                yield return new object[] { "ice+universal://host.zeroc.com:10000/identity?transport=tcp" };
                yield return new object[] { "ice+universal://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar" };
                yield return new object[] { "ice:tcp -p 10000" }; // a valid URI
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                throw new System.NotImplementedException();
            }
        }
    }
}
