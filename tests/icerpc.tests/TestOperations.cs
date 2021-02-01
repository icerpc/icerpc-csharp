using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Ice.Tests.Operations
{

    [TestFixture(Protocol.Ice2, "tcp", Category = "Ice2")]
    [TestFixture(Protocol.Ice1, "tcp", Category = "Ice1")]
    [Parallelizable]
    public class OperationsTest : FunctionalTest
    {
        ITesterPrx? _prx;
        public OperationsTest(Protocol protocol, string transport) : base(protocol, transport)
        {
        }

        [OneTimeSetUp]
        public async Task InitializeAsync()
        {
            ObjectAdapter.Add("test", new Tester());
            await ObjectAdapter.ActivateAsync();
            _prx = ITesterPrx.Parse(GetTestProxy("test"), Communicator);
        }

        public async Task OperationsOpVoid()
        {
            await _prx!.OpVoidAsync();
        }

        [TestCase(127)]
        public async Task OperationsOpByte(byte value)
        {
            // Exercise
            var result = await _prx!.OpByteAsync(value);

            // Assert
            Assert.AreEqual(value, result);
        }

        [TestCase(1024)]
        public async Task OperationsOpShort(short value)
        {
            // Exercise
            var result = await _prx!.OpShortAsync(value);

            // Assert
            Assert.AreEqual(value, result);
        }

        [TestCase("hello")]
        public async Task OperationsOpString(string value)
        {
            // Exercise
            var result = await _prx!.OpStringAsync(value);

            // Assert
            Assert.AreEqual(value, result);
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
