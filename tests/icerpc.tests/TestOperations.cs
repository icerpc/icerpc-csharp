using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;
using ZeroC.Ice;

namespace IceRPC.Ice.Tests.Operations
{

    public class OperationsTest : FunctionalTest
    {
        [Theory]
        [InlineData(Protocol.Ice2)]
        [InlineData(Protocol.Ice1)]
        public async Task OperationsOpVoid(Protocol protocol)
        {
            await using (await CreateServerAsync<Tester>(protocol, "test", new Tester()))
            {
                ITesterPrx prx = CreateClient(ITesterPrx.Factory, protocol, "test");
                await prx.OpVoidAsync();
            }
        }

        [Theory]
        [InlineData(Protocol.Ice2, "hello")]
        [InlineData(Protocol.Ice1, "hello")]
        public async Task OperationsOpStringstring(Protocol protocol, string value)
        {
            await using (await CreateServerAsync<Tester>(protocol, "test", new Tester()))
            {
                ITesterPrx prx = CreateClient(ITesterPrx.Factory, protocol, "test");
                var result = await prx.OpStringAsync(value);
                Assert.Equal(value, result);
            }
        }
    }

    public class Tester : IAsyncTester
    {
        public ValueTask OpVoidAsync(Current current, CancellationToken cancel) => default;
        public ValueTask<string> OpStringAsync(string str, Current current, CancellationToken cancel) =>
            new ValueTask<string>(str);
    }
}