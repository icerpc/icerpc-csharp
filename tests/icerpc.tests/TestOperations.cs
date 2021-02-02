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
            // Setup
            await using ObjectAdapter adapter = Communicator.CreateObjectAdapterWithEndpoints(
                "TestAdapter",
                GetTestEndpoint(protocol, "tcp"));
            adapter.Add("test", new Tester());
            await adapter.ActivateAsync();
            ITesterPrx prx = ITesterPrx.Parse(GetTestProxy(protocol, "tcp", "test"), Communicator);

            // Exercise
            await prx.OpVoidAsync();
        }

        [Theory]
        [InlineData(Protocol.Ice2, 127)]
        [InlineData(Protocol.Ice1, 127)]
        public async Task OperationsOpByte(Protocol protocol, byte value)
        {
            // Setup
            await using ObjectAdapter adapter = Communicator.CreateObjectAdapterWithEndpoints(
                "TestAdapter",
                GetTestEndpoint(protocol, "tcp"));
            adapter.Add("test", new Tester());
            await adapter.ActivateAsync();
            ITesterPrx prx = ITesterPrx.Parse(GetTestProxy(protocol, "tcp", "test"), Communicator);

            // Exercise
            var result = await prx.OpByteAsync(value);

            // Assert
            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData(Protocol.Ice2, 1024)]
        [InlineData(Protocol.Ice1, 1024)]
        public async Task OperationsOpShort(Protocol protocol, short value)
        {
            // Setup
            await using ObjectAdapter adapter = Communicator.CreateObjectAdapterWithEndpoints(
                "TestAdapter",
                GetTestEndpoint(protocol, "tcp"));
            adapter.Add("test", new Tester());
            await adapter.ActivateAsync();
            ITesterPrx prx = ITesterPrx.Parse(GetTestProxy(protocol, "tcp", "test"), Communicator);

            // Exercise
            var result = await prx.OpShortAsync(value);

            // Assert
            Assert.Equal(value, result);
        }

        [Theory]
        [InlineData(Protocol.Ice2, "hello")]
        [InlineData(Protocol.Ice1, "hello")]
        public async Task OperationsOpString(Protocol protocol, string value)
        {
            // Setup
            await using ObjectAdapter adapter = Communicator.CreateObjectAdapterWithEndpoints(
                "TestAdapter",
                GetTestEndpoint(protocol, "tcp"));
            adapter.Add("test", new Tester());
            await adapter.ActivateAsync();
            ITesterPrx prx = ITesterPrx.Parse(GetTestProxy(protocol, "tcp", "test"), Communicator);

            // Exercise
            var result = await prx.OpStringAsync(value);

            // Assert
            Assert.Equal(value, result);
        }
    }

    public class Tester : IAsyncTester
    {
        public ValueTask OpVoidAsync(Current current, CancellationToken cancel) => default;

        public ValueTask<byte> OpByteAsync(byte value, Current current, CancellationToken cancel) =>
            new ValueTask<byte>(value);

        public ValueTask<short> OpShortAsync(short value, Current current, CancellationToken cancel) =>
            new ValueTask<short>(value);

        public ValueTask<string> OpStringAsync(string value, Current current, CancellationToken cancel) =>
            new ValueTask<string>(value);
    }
}
