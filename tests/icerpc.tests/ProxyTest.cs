using System;
using System.Collections;
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


        [Theory]
        [ClassData(typeof(TestProxyParsingData))]
        public void TestProxyParsing(string str)
        {
            var prx = IObjectPrx.Parse(str, _fixture.Communicator);
            Assert.Equal(Protocol.Ice2, prx.Protocol);
            _output.WriteLine($"{str} = {prx}");
            var prx2 = IObjectPrx.Parse(prx.ToString()!, _fixture.Communicator);
            Assert.Equal(prx, prx2); // round-trip works
        }

        public class TestProxyParsingData : TheoryData<string>
        {
            public TestProxyParsingData()
            {
                Add("ice+tcp://host.zeroc.com/identity#facet");
                Add("ice+tcp://host.zeroc.com:1000/category/name");
                Add("ice+tcp://host.zeroc.com:1000/loc0/loc1/category/name");
                Add("ice+tcp://host.zeroc.com/category/name%20with%20space");
                Add("ice+ws://host.zeroc.com//identity");
                Add("ice+ws://host.zeroc.com//identity?invocation-timeout=100ms");
                Add("ice+ws://host.zeroc.com//identity?invocation-timeout=1s");
                Add("ice+ws://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com");
                Add("ice+ws://host.zeroc.com//identity?alt-endpoint=host2.zeroc.com:10000");
                Add("ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000,host2,host3,host4");
                Add("ice+tcp://[::1]:10000/identity?alt-endpoint=host1:10000&alt-endpoint=host2,host3&alt-endpoint=[::2]");
                Add("ice:location//identity#facet");
                Add("ice:location//identity?relative=true#facet");
                Add("ice+tcp://host.zeroc.com//identity");
                Add("ice+tcp://host.zeroc.com:/identity"); // another syntax for empty port
                Add("ice+universal://com.zeroc.ice/identity?transport=iaps&option=a,b%2Cb,c&option=d");
                Add("ice+universal://host.zeroc.com/identity?transport=100");
                Add("ice+universal://[::ab:cd:ef:00]/identity?transport=bt"); // leading :: to make the address IPv6-like
                Add("ice+ws://host.zeroc.com/identity?resource=/foo%2Fbar?/xyz");
                Add("ice+universal://host.zeroc.com:10000/identity?transport=tcp");
                Add("ice+universal://host.zeroc.com/identity?transport=ws&option=/foo%2520/bar");
                Add("ice:tcp -p 10000"); // a valid URI
            }
        }
    }
}
