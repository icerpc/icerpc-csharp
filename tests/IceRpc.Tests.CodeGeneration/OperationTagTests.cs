// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using NUnit.Framework;

namespace IceRpc.Tests.CodeGeneration
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public sealed class OperationTagTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly OperationTagDoublePrx _doublePrx;
        private readonly OperationTagMarshaledResultPrx _marshaledResultPrx;

        private readonly OperationTagPrx _prx;

        public OperationTagTests()
        {
            var router = new Router();
            router.Map<IOperationTagDouble>(new OperationTagDouble());
            router.Map<IOperationTagMarshaledResult>(new OperationTagMarshaledResult());
             router.Map<IOperationTag>(new OperationTag());

            _server = new Server
            {
                Dispatcher = router,
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            _server.Listen();
            _connection = new Connection
            {
                RemoteEndpoint = _server.Endpoint
            };
            _doublePrx = OperationTagDoublePrx.FromConnection(_connection);
            _marshaledResultPrx = OperationTagMarshaledResultPrx.FromConnection(_connection);
            _prx = OperationTagPrx.FromConnection(_connection);
        }

        [OneTimeTearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Test]
        public async Task Tagged_Double()
        {
            {
                (byte? r1, byte? r2) = await _doublePrx.OpByteAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _doublePrx.OpByteAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (bool? r1, bool? r2) = await _doublePrx.OpBoolAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _doublePrx.OpBoolAsync(true);
                Assert.That(r1, Is.True);
                Assert.That(r2, Is.True);
            }

            {
                (short? r1, short? r2) = await _doublePrx.OpShortAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _doublePrx.OpShortAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (int? r1, int? r2) = await _doublePrx.OpIntAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _doublePrx.OpIntAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (long? r1, long? r2) = await _doublePrx.OpLongAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _doublePrx.OpLongAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (float? r1, float? r2) = await _doublePrx.OpFloatAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _doublePrx.OpFloatAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (double? r1, double? r2) = await _doublePrx.OpDoubleAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _doublePrx.OpDoubleAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (string? r1, string? r2) = await _doublePrx.OpStringAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _doublePrx.OpStringAsync("hello");
                Assert.AreEqual("hello", r1);
                Assert.AreEqual("hello", r2);
            }

            {
                (MyEnum? r1, MyEnum? r2) = await _doublePrx.OpMyEnumAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _doublePrx.OpMyEnumAsync(MyEnum.enum1);
                Assert.AreEqual(MyEnum.enum1, r1);
                Assert.AreEqual(MyEnum.enum1, r2);
            }

            {
                (MyStruct? r1, MyStruct? r2) = await _doublePrx.OpMyStructAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyStruct(1, 1);
                (r1, r2) = await _doublePrx.OpMyStructAsync(p1);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p1, r2);
            }

            {
                (AnotherStruct? r1, AnotherStruct? r2) = await _doublePrx.OpAnotherStructAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new AnotherStruct(
                    "hello",
                    OperationsPrx.Parse("ice+tcp://localhost/hello"),
                    MyEnum.enum1,
                    new MyStruct(1, 1));
                (r1, r2) = await _doublePrx.OpAnotherStructAsync(p1);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p1, r2);
            }

            {
                (byte[]? r1, byte[]? r2) = await _doublePrx.OpByteSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                byte[] p1 = new byte[] { 42 };
                (r1, r2) = await _doublePrx.OpByteSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<byte>? r1, List<byte>? r2) = await _doublePrx.OpByteListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<byte> { 42 };
                (r1, r2) = await _doublePrx.OpByteListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (byte?[]? r1, byte?[]? r2) = await _doublePrx.OpOptionalByteSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                byte?[] p1 = new byte?[] { 42, null, 43 };
                (r1, r2) = await _doublePrx.OpOptionalByteSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (bool[]? r1, bool[]? r2) = await _doublePrx.OpBoolSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                bool[] p1 = new bool[] { true };
                (r1, r2) = await _doublePrx.OpBoolSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<bool>? r1, List<bool>? r2) = await _doublePrx.OpBoolListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<bool> { true };
                (r1, r2) = await _doublePrx.OpBoolListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (bool?[]? r1, bool?[]? r2) = await _doublePrx.OpOptionalBoolSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                bool?[] p1 = new bool?[] { true, null, false };
                (r1, r2) = await _doublePrx.OpOptionalBoolSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (short[]? r1, short[]? r2) = await _doublePrx.OpShortSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                short[] p1 = new short[] { 42 };
                (r1, r2) = await _doublePrx.OpShortSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<short>? r1, List<short>? r2) = await _doublePrx.OpShortListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<short> { 42 };
                (r1, r2) = await _doublePrx.OpShortListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (short?[]? r1, short?[]? r2) = await _doublePrx.OpOptionalShortSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                short?[] p1 = new short?[] { 42, null, 34 };
                (r1, r2) = await _doublePrx.OpOptionalShortSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (int[]? r1, int[]? r2) = await _doublePrx.OpIntSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                int[] p1 = new int[] { 42 };
                (r1, r2) = await _doublePrx.OpIntSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<int>? r1, List<int>? r2) = await _doublePrx.OpIntListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<int> { 42 };
                (r1, r2) = await _doublePrx.OpIntListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (int?[]? r1, int?[]? r2) = await _doublePrx.OpOptionalIntSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                int?[]? p1 = new int?[] { 42, null, 43 };
                (r1, r2) = await _doublePrx.OpOptionalIntSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (long[]? r1, long[]? r2) = await _doublePrx.OpLongSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                long[] p1 = new long[] { 42 };
                (r1, r2) = await _doublePrx.OpLongSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<long>? r1, List<long>? r2) = await _doublePrx.OpLongListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<long> { 42 };
                (r1, r2) = await _doublePrx.OpLongListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (long?[]? r1, long?[]? r2) = await _doublePrx.OpOptionalLongSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                long?[] p1 = new long?[] { 42, null, 43 };
                (r1, r2) = await _doublePrx.OpOptionalLongSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (float[]? r1, float[]? r2) = await _doublePrx.OpFloatSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                float[] p1 = new float[] { 42 };
                (r1, r2) = await _doublePrx.OpFloatSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<float>? r1, List<float>? r2) = await _doublePrx.OpFloatListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<float> { 42 };
                (r1, r2) = await _doublePrx.OpFloatListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (float?[]? r1, float?[]? r2) = await _doublePrx.OpOptionalFloatSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                float?[] p1 = new float?[] { 42, null, 43 };
                (r1, r2) = await _doublePrx.OpOptionalFloatSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (double[]? r1, double[]? r2) = await _doublePrx.OpDoubleSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                double[] p1 = new double[] { 42 };
                (r1, r2) = await _doublePrx.OpDoubleSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<double>? r1, List<double>? r2) = await _doublePrx.OpDoubleListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<double> { 42 };
                (r1, r2) = await _doublePrx.OpDoubleListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (double?[]? r1, double?[]? r2) = await _doublePrx.OpOptionalDoubleSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                double?[] p1 = new double?[] { 42 };
                (r1, r2) = await _doublePrx.OpOptionalDoubleSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (string[]? r1, string[]? r2) = await _doublePrx.OpStringSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                string[] p1 = new string[] { "hello" };
                (r1, r2) = await _doublePrx.OpStringSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<string>? r1, List<string>? r2) = await _doublePrx.OpStringListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<string> { "hello" };
                (r1, r2) = await _doublePrx.OpStringListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (string?[]? r1, string?[]? r2) = await _doublePrx.OpOptionalStringSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                string?[] p1 = new string?[] { "hello" };
                (r1, r2) = await _doublePrx.OpOptionalStringSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (MyStruct[]? r1, MyStruct[]? r2) = await _doublePrx.OpMyStructSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyStruct[] { new MyStruct(1, 1) };
                (r1, r2) = await _doublePrx.OpMyStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<MyStruct>? r1, List<MyStruct>? r2) = await _doublePrx.OpMyStructListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<MyStruct> { new MyStruct(1, 1) };
                (r1, r2) = await _doublePrx.OpMyStructListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (MyStruct?[]? r1, MyStruct?[]? r2) = await _doublePrx.OpOptionalMyStructSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyStruct?[] { new MyStruct(1, 1), null, new MyStruct(1, 1) };
                (r1, r2) = await _doublePrx.OpOptionalMyStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (AnotherStruct[]? r1, AnotherStruct[]? r2) = await _doublePrx.OpAnotherStructSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new AnotherStruct[]
                {
                    new AnotherStruct(
                        "hello",
                        OperationsPrx.Parse("ice+tcp://localhost/hello"),
                        MyEnum.enum1,
                        new MyStruct(1, 1))
                };
                (r1, r2) = await _doublePrx.OpAnotherStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<AnotherStruct>? r1, List<AnotherStruct>? r2) = await _doublePrx.OpAnotherStructListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<AnotherStruct>
                {
                    new AnotherStruct(
                        "hello",
                        OperationsPrx.Parse("ice+tcp://localhost/hello"),
                        MyEnum.enum1,
                        new MyStruct(1, 1))
                };
                (r1, r2) = await _doublePrx.OpAnotherStructListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (AnotherStruct?[]? r1, AnotherStruct?[]? r2) = await _doublePrx.OpOptionalAnotherStructSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new AnotherStruct?[]
                {
                    new AnotherStruct(
                        "hello",
                        OperationsPrx.Parse("ice+tcp://localhost/hello"),
                        MyEnum.enum1,
                        new MyStruct(1, 1)),
                    null,
                    new AnotherStruct(
                        "hello",
                        OperationsPrx.Parse("ice+tcp://localhost/hello"),
                        MyEnum.enum1,
                        new MyStruct(1, 1)),

                };
                (r1, r2) = await _doublePrx.OpOptionalAnotherStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (Dictionary<int, int>? r1, Dictionary<int, int>? r2) = await _doublePrx.OpIntDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<int, int> { { 1, 1 } };
                (r1, r2) = await _doublePrx.OpIntDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (SortedDictionary<int, int>? r1, SortedDictionary<int, int>? r2) = await _doublePrx.OpIntSortedDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new SortedDictionary<int, int> { { 1, 1 } };
                (r1, r2) = await _doublePrx.OpIntSortedDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (Dictionary<int, int?>? r1, Dictionary<int, int?>? r2) = await _doublePrx.OpOptionalIntDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<int, int?> { { 1, 1 } };
                (r1, r2) = await _doublePrx.OpOptionalIntDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (SortedDictionary<int, int?>? r1, SortedDictionary<int, int?>? r2) =
                    await _doublePrx.OpOptionalIntSortedDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new SortedDictionary<int, int?> { { 1, 1 } };
                (r1, r2) = await _doublePrx.OpOptionalIntSortedDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (Dictionary<string, string>? r1, Dictionary<string, string>? r2) = await _doublePrx.OpStringDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<string, string> { { "a", "b" } };
                (r1, r2) = await _doublePrx.OpStringDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (SortedDictionary<string, string>? r1, SortedDictionary<string, string>? r2) =
                    await _doublePrx.OpStringSortedDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new SortedDictionary<string, string> { { "a", "b" } };
                (r1, r2) = await _doublePrx.OpStringSortedDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (Dictionary<string, string?>? r1, Dictionary<string, string?>? r2) =
                    await _doublePrx.OpOptionalStringDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<string, string?> { { "a", "b" } };
                (r1, r2) = await _doublePrx.OpOptionalStringDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (SortedDictionary<string, string?>? r1, SortedDictionary<string, string?>? r2) =
                    await _doublePrx.OpOptionalStringSortedDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new SortedDictionary<string, string?> { { "a", "b" } };
                (r1, r2) = await _doublePrx.OpOptionalStringSortedDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }
        }

        [Test]
        public async Task Tagged_MarshaledResult()
        {
            {
                MyStruct? r1 = await _marshaledResultPrx.OpMyStructMarshaledResultAsync(null);
                Assert.That(r1, Is.Null);

                var p1 = new MyStruct(1, 1);
                r1 = await _marshaledResultPrx.OpMyStructMarshaledResultAsync(p1);
                Assert.AreEqual(p1, r1);
            }

            {
                Dictionary<int, int>? r1 = await _marshaledResultPrx.OpIntDictMarshaledResultAsync(null);
                Assert.That(r1, Is.Null);

                var p1 = new Dictionary<int, int> { { 1, 1 } };
                r1 = await _marshaledResultPrx.OpIntDictMarshaledResultAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
            }

            {
                string[]? r1 = await _marshaledResultPrx.OpStringSeqMarshaledResultAsync(null);
                Assert.That(r1, Is.Null);

                string[] p1 = new string[] { "hello" };
                r1 = await _marshaledResultPrx.OpStringSeqMarshaledResultAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
            }
        }

        [Test]
        public async Task Tagged_DuplicateTag()
        {
            // Build a request payload with 2 tagged values
            ReadOnlyMemory<ReadOnlyMemory<byte>> requestPayload =
                _prx.Proxy.GetIceEncoding().CreatePayloadFromArgs(
                    (15, "test"),
                    (IceEncoder encoder, in (int? N, string? S) value) =>
                    {
                        if (value.N != null)
                        {
                            encoder.EncodeTagged(1,
                                                 TagFormat.F4,
                                                 size: 4,
                                                 value.N.Value,
                                                 (encoder, v) => encoder.EncodeInt(v));
                        }
                        if (value.S != null)
                        {
                            encoder.EncodeTagged(1, // duplicate tag ignored by the server
                                                 TagFormat.OVSize,
                                                 value.S,
                                                 (encoder, v) => encoder.EncodeString(v));
                        }
                    });

            (IncomingResponse response, StreamParamReceiver? _) =
                await _prx.Proxy.InvokeAsync("opVoid", _prx.Proxy.Encoding, requestPayload);

            Assert.DoesNotThrow(() => response.CheckVoidReturnValue(
                _prx.Proxy.Invoker,
                response.GetIceDecoderFactory(new DefaultIceDecoderFactories(typeof(OperationTagTests).Assembly))));
        }
    }

    public class OperationTagDouble : Service, IOperationTagDouble
    {
        public ValueTask<(AnotherStruct? R1, AnotherStruct? R2)> OpAnotherStructAsync(
            AnotherStruct? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<AnotherStruct>? R1, IEnumerable<AnotherStruct>? R2)> OpAnotherStructListAsync(
            List<AnotherStruct>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<AnotherStruct>? R1, IEnumerable<AnotherStruct>? R2)> OpAnotherStructSeqAsync(
            AnotherStruct[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<AnotherStruct?>? R1, IEnumerable<AnotherStruct?>? R2)> OpOptionalAnotherStructSeqAsync(
            AnotherStruct?[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(bool? R1, bool? R2)> OpBoolAsync(
            bool? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<bool>? R1, IEnumerable<bool>? R2)> OpBoolListAsync(
            List<bool>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<bool> R1, ReadOnlyMemory<bool> R2)> OpBoolSeqAsync(
            bool[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<bool?>? R1, IEnumerable<bool?>? R2)> OpOptionalBoolSeqAsync(
            bool?[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(byte? R1, byte? R2)> OpByteAsync(
            byte? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<byte>? R1, IEnumerable<byte>? R2)> OpByteListAsync(
            List<byte>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<byte> R1, ReadOnlyMemory<byte> R2)> OpByteSeqAsync(
            byte[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<byte?>? R1, IEnumerable<byte?>? R2)> OpOptionalByteSeqAsync(
            byte?[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(double? R1, double? R2)> OpDoubleAsync(
            double? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<double>? R1, IEnumerable<double>? R2)> OpDoubleListAsync(
            List<double>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<double> R1, ReadOnlyMemory<double> R2)> OpDoubleSeqAsync(
            double[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<double?>? R1, IEnumerable<double?>? R2)> OpOptionalDoubleSeqAsync(
            double?[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(float? R1, float? R2)> OpFloatAsync(
            float? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<float>? R1, IEnumerable<float>? R2)> OpFloatListAsync(
            List<float>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<float> R1, ReadOnlyMemory<float> R2)> OpFloatSeqAsync(
            float[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<float?>? R1, IEnumerable<float?>? R2)> OpOptionalFloatSeqAsync(
            float?[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(int? R1, int? R2)> OpIntAsync(
            int? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<int, int>>? R1, IEnumerable<KeyValuePair<int, int>>? R2)> OpIntDictAsync(
            Dictionary<int, int>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<int, int>>? R1, IEnumerable<KeyValuePair<int, int>>? R2)> OpIntSortedDictAsync(
            SortedDictionary<int, int>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<int, int?>>? R1, IEnumerable<KeyValuePair<int, int?>>? R2)> OpOptionalIntDictAsync(
            Dictionary<int, int?>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<int, int?>>? R1, IEnumerable<KeyValuePair<int, int?>>? R2)> OpOptionalIntSortedDictAsync(
            SortedDictionary<int, int?>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<int>? R1, IEnumerable<int>? R2)> OpIntListAsync(
            List<int>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<int> R1, ReadOnlyMemory<int> R2)> OpIntSeqAsync(
            int[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<int?>? R1, IEnumerable<int?>? R2)> OpOptionalIntSeqAsync(
            int?[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(long? R1, long? R2)> OpLongAsync(
            long? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<long>? R1, IEnumerable<long>? R2)> OpLongListAsync(
            List<long>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<long> R1, ReadOnlyMemory<long> R2)> OpLongSeqAsync(
            long[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<long?>? R1, IEnumerable<long?>? R2)> OpOptionalLongSeqAsync(
            long?[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(MyEnum? R1, MyEnum? R2)> OpMyEnumAsync(
            MyEnum? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(MyStruct? R1, MyStruct? R2)> OpMyStructAsync(
            MyStruct? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyStruct>? R1, IEnumerable<MyStruct>? R2)> OpMyStructListAsync(
            List<MyStruct>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyStruct>? R1, IEnumerable<MyStruct>? R2)> OpMyStructSeqAsync(
            MyStruct[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyStruct?>? R1, IEnumerable<MyStruct?>? R2)> OpOptionalMyStructSeqAsync(
            MyStruct?[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(short? R1, short? R2)> OpShortAsync(
            short? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<short>? R1, IEnumerable<short>? R2)> OpShortListAsync(
            List<short>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<short> R1, ReadOnlyMemory<short> R2)> OpShortSeqAsync(
            short[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<short?>? R1, IEnumerable<short?>? R2)> OpOptionalShortSeqAsync(
            short?[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(string? R1, string? R2)> OpStringAsync(
            string? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<string, string>>? R1, IEnumerable<KeyValuePair<string, string>>? R2)> OpStringDictAsync(
            Dictionary<string, string>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<string, string>>? R1, IEnumerable<KeyValuePair<string, string>>? R2)> OpStringSortedDictAsync(
            SortedDictionary<string, string>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<string, string?>>? R1, IEnumerable<KeyValuePair<string, string?>>? R2)> OpOptionalStringDictAsync(
            Dictionary<string, string?>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<string, string?>>? R1, IEnumerable<KeyValuePair<string, string?>>? R2)> OpOptionalStringSortedDictAsync(
            SortedDictionary<string, string?>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<string>? R1, IEnumerable<string>? R2)> OpStringListAsync(
            List<string>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<string>? R1, IEnumerable<string>? R2)> OpStringSeqAsync(
            string[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<string?>? R1, IEnumerable<string?>? R2)> OpOptionalStringSeqAsync(
            string?[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyEnum>? R1, IEnumerable<MyEnum>? R2)> OpMyEnumSeqAsync(
            MyEnum[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyEnum?>? R1, IEnumerable<MyEnum?>? R2)> OpOptionalMyEnumSeqAsync(
            MyEnum?[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyEnum>? R1, IEnumerable<MyEnum>? R2)> OpMyEnumListAsync(
            List<MyEnum>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<MyFixedLengthEnum> R1, ReadOnlyMemory<MyFixedLengthEnum> R2)> OpMyFixedLengthEnumSeqAsync(
            MyFixedLengthEnum[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyFixedLengthEnum>? R1, IEnumerable<MyFixedLengthEnum>? R2)> OpMyFixedLengthEnumListAsync(
            List<MyFixedLengthEnum>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyFixedLengthEnum?>? R1, IEnumerable<MyFixedLengthEnum?>? R2)> OpOptionalMyFixedLengthEnumSeqAsync(
            MyFixedLengthEnum?[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));
    }

    public class OperationTagMarshaledResult : Service, IOperationTagMarshaledResult
    {
        public ValueTask<IOperationTagMarshaledResult.OpIntDictMarshaledResultMarshaledReturnValue> OpIntDictMarshaledResultAsync(
            Dictionary<int, int>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(new IOperationTagMarshaledResult.OpIntDictMarshaledResultMarshaledReturnValue(p1, dispatch));

        public ValueTask<IOperationTagMarshaledResult.OpStringSeqMarshaledResultMarshaledReturnValue> OpStringSeqMarshaledResultAsync(
            string[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(new IOperationTagMarshaledResult.OpStringSeqMarshaledResultMarshaledReturnValue(p1, dispatch));
        public ValueTask<IOperationTagMarshaledResult.OpMyStructMarshaledResultMarshaledReturnValue> OpMyStructMarshaledResultAsync(
            MyStruct? p1,
            Dispatch dispatch,
            CancellationToken cancel) =>
            new(new IOperationTagMarshaledResult.OpMyStructMarshaledResultMarshaledReturnValue(p1, dispatch));
    }

    public class OperationTag : Service, IOperationTag
    {
        public ValueTask OpVoidAsync(Dispatch dispatch, CancellationToken cancel) => default;
    }
}
