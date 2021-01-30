using System.Threading;
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

        [Theory]
        [InlineData("Default", Protocol.Ice2, "tcp", "localhost")]
        [InlineData("Ipv6", Protocol.Ice1, "tcp", "::1")]
        public async Task Operations(string name, Protocol protocol, string transport, string host)
        {
            ObjectAdapter adapter = _fixture.Communicator.CreateObjectAdapterWithEndpoints(
                $"TestAdapter{name}",
                _fixture.GetTestEndpoint(protocol, transport, host));
            adapter.Add("test", new Tester());
            await adapter.ActivateAsync();

            ITesterPrx prx = ITesterPrx.Parse(_fixture.GetTestProxy(protocol, transport, host, "test"), _fixture.Communicator);

            await prx.OpVoidAsync();
            await adapter.ShutdownAsync();

            await adapter.ShutdownComplete;
        }
    }

    public class Tester : IAsyncTester
    {
        ValueTask IAsyncTester.OpVoidAsync(Current current, CancellationToken cancel) => default;
    }
}