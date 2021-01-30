using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ZeroC.Ice;

namespace IceRPC.Ice.Tests.Operations
{

    public class OperationsTest : IClassFixture<TestFixture>
    {
        private readonly ITestOutputHelper _output;
        private TestFixture _fixture;

        public OperationsTest(ITestOutputHelper output, TestFixture fixture)
        {
            _output = output;
            _fixture = fixture;
        }

        [Fact]
        [InlineData(Protocol.Ice2, "tcp", "localhost")]
        public async Task Operations(Protocol protocol, string transport, string host)
        {
            ObjectAdapter adapter = Communicator.CreateObjectAdapterWithEndpoints(
                "TestAdapter",
                GetTestEndpoint(protocol, host));
            adapter.Add("test", new Tester());
            await adapter.ActivateAsync();

            ITesterPrx prx = ITesterPrx.Parse(GetTestProxy(Protocol.Ice2, protocol, host, "test"), Communicator);

            await prx.OpVoidAsync();
        }
    }

    public class Tester : ITester
    {
        Task OpVoidAsync() => Task.CompletedTask; 
    }
}