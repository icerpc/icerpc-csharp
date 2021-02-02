// Copyright (c) ZeroC, Inc. All rights reserved.

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using ZeroC.Ice;

namespace IceRpc.Tests
{

    [TestFixture(Protocol.Ice2, Category = "Ice2")]
    [TestFixture(Protocol.Ice1, Category = "Ice1")]
    [Parallelizable]
    public class OperationsTest : FunctionalTest
    {
        private Operations.ITestServicePrx _prx;
        public OperationsTest(Protocol protocol) : base(protocol, "") => _prx = null!;

        [OneTimeSetUp]
        public async Task InitializeAsync()
        {
            ObjectAdapter.Add("test", new TestService());
            await ObjectAdapter.ActivateAsync();
            _prx = Operations.ITestServicePrx.Parse(GetTestProxy("test"), Communicator);
        }

        public async Task OpVoid() => await _prx.OpVoidAsync();

        [TestCase(0xff, 0x0f)]
        public async Task OpByteAsync(byte p1, byte p2)
        {
            (byte r1, byte r2) = await _prx.OpByteAsync(p1, p2);
            Assert.AreEqual(p1, r1);
            Assert.AreEqual(p2, r2);
        }

        [TestCase(true, false)]
        [TestCase(false, true)]
        public async Task OpBoolAsync(bool p1, bool p2)
        {
            (bool r1, bool r2) = await _prx.OpBoolAsync(p1, p2);
            Assert.AreEqual(p1, r1);
            Assert.AreEqual(p2, r2);
        }

        [TestCase(10, 11, 12)]
        [TestCase(short.MaxValue, int.MaxValue, long.MaxValue)]
        [TestCase(short.MinValue, int.MinValue, long.MinValue)]
        public async Task OpShortIntLongAsync(short p1, int p2, long p3)
        {
            (long r1, short r2, int r3, long r4) = await _prx.OpShortIntLongAsync(p1, p2, p3);

            Assert.AreEqual(r2, p1);
            Assert.AreEqual(r3, p2);
            Assert.AreEqual(r4, p3);
            Assert.AreEqual(r1, p3);
        }

        [TestCase((ushort)10, (uint)11, (ulong)12)]
        [TestCase(ushort.MaxValue, uint.MaxValue, ulong.MaxValue)]
        [TestCase(ushort.MinValue, uint.MinValue, ulong.MinValue)]
        public async Task OpUShortUIntULongAsync(ushort p1, uint p2, ulong p3)
        {
            (ulong r1, ushort r2, uint r3, ulong r4) = await _prx.OpUShortUIntULongAsync(p1, p2, p3);

            Assert.AreEqual(r2, p1);
            Assert.AreEqual(r3, p2);
            Assert.AreEqual(r4, p3);
            Assert.AreEqual(r1, p3);
        }

        [TestCase(int.MaxValue)]
        [TestCase(0)]
        [TestCase(int.MinValue)]
        public async Task OpVarIntAsync(int p1)
        {
            var r1 = await _prx.OpVarIntAsync(p1);
            Assert.AreEqual(r1, p1);
        }

        [TestCase(uint.MaxValue)]
        [TestCase(uint.MinValue)]
        public async Task OpVarUIntAsync(uint p1)
        {
            var r1 = await _prx.OpVarUIntAsync(p1);
            Assert.AreEqual(r1, p1);
        }

        [TestCase(EncodingDefinitions.VarLongMaxValue)]
        [TestCase(0)]
        [TestCase(EncodingDefinitions.VarLongMinValue)]
        public async Task OpVarLongAsync(long p1)
        {
            var r1 = await _prx.OpVarLongAsync(p1);
            Assert.AreEqual(r1, p1);
        }

        [TestCase(EncodingDefinitions.VarULongMaxValue)]
        [TestCase(EncodingDefinitions.VarULongMinValue)]
        public async Task OpVarULongAsync(ulong p1)
        {
            var r1 = await _prx.OpVarULongAsync(p1);
            Assert.AreEqual(r1, p1);
        }

        [TestCase(3.14f, 1.1E10)]
        [TestCase(float.MaxValue, double.MaxValue)]
        [TestCase(float.MinValue, double.MinValue)]
        public async Task OpFloatDoubleAsync(float p1, double p2)
        {
            (double r1, float r2, double r3) = await _prx.OpFloatDoubleAsync(p1, p2);

            Assert.AreEqual(r1, p2);
            Assert.AreEqual(r2, p1);
            Assert.AreEqual(r3, p2);
        }

        [TestCase(Operations.MyEnum.enum1)]
        [TestCase(Operations.MyEnum.enum2)]
        [TestCase(Operations.MyEnum.enum3)]
        public async Task OpMyEnum(Operations.MyEnum p1)
        {
            (Operations.MyEnum r1, Operations.MyEnum r2) = await _prx.OpMyEnumAsync(p1);
            Assert.AreEqual(Operations.MyEnum.enum3, r1);
            Assert.AreEqual(p1, r2);
        }

        [TestCase(1024)]
        [TestCase(short.MaxValue)]
        [TestCase(short.MinValue)]
        public async Task OpShort(short value) => Assert.AreEqual(value, await _prx.OpShortAsync(value));

        [TestCase("hello")]
        public async Task OpString(string value) => Assert.AreEqual(value, await _prx.OpStringAsync(value));
    }

    public class TestService : Operations.IAsyncTestService
    {
        public ValueTask OpVoidAsync(Current current, CancellationToken cancel) => default;

        public ValueTask<(byte, byte)> OpByteAsync(byte p1, byte p2, Current current, CancellationToken cancel) =>
            new ValueTask<(byte, byte)>((p1, p2));

        public ValueTask<(bool, bool)> OpBoolAsync(bool p1, bool p2, Current current, CancellationToken cancel) =>
            new ValueTask<(bool, bool)>((p1, p2));

        public ValueTask<(long, short, int, long)> OpShortIntLongAsync(
           short p1,
           int p2,
           long p3,
           Current current,
           CancellationToken cancel) =>
           new((p3, p1, p2, p3));

        public ValueTask<(ulong, ushort, uint, ulong)> OpUShortUIntULongAsync(
            ushort p1,
            uint p2,
            ulong p3,
            Current current,
            CancellationToken cancel) =>
            new((p3, p1, p2, p3));

        public ValueTask<int> OpVarIntAsync(int v, Current current, CancellationToken cancel) => new(v);
        public ValueTask<uint> OpVarUIntAsync(uint v, Current current, CancellationToken cancel) => new(v);
        public ValueTask<long> OpVarLongAsync(long v, Current current, CancellationToken cancel) => new(v);
        public ValueTask<ulong> OpVarULongAsync(ulong v, Current current, CancellationToken cancel) => new(v);

        public ValueTask<short> OpShortAsync(short value, Current current, CancellationToken cancel) =>
            new ValueTask<short>(value);

        public ValueTask<(double, float, double)> OpFloatDoubleAsync(
            float p1,
            double p2,
            Current current,
            CancellationToken cancel) =>
            new((p2, p1, p2));

        public ValueTask<(Operations.MyEnum, Operations.MyEnum)> OpMyEnumAsync(
            Operations.MyEnum p1, Current current, CancellationToken cancel) =>
            new((Operations.MyEnum.enum3, p1));

        public ValueTask<string> OpStringAsync(string value, Current current, CancellationToken cancel) =>
            new ValueTask<string>(value);
    }
}
