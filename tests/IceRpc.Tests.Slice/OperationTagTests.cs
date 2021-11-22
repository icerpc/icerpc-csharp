// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public sealed class OperationTagTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;

        public OperationTagTests()
        {
            var router = new Router();
            router.Map<IOperationTagDouble>(new OperationTagDouble());
            router.Map<IOperationTagEncodedResult>(new OperationTagEncodedResult());
            router.Map<IOperationTag>(new OperationTag());

            _server = new Server
            {
                Dispatcher = router,
                Endpoint = TestHelper.GetUniqueColocEndpoint(),
            };
            _server.Listen();
            _connection = new Connection { RemoteEndpoint = _server.Endpoint };
        }

        [OneTimeTearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [TestCase("1.1")]
        [TestCase("2.0")]
        public async Task OperationTag_Double(string encoding)
        {
            var doublePrx = OperationTagDoublePrx.FromConnection(_connection);
            doublePrx.Proxy.Encoding = Encoding.FromString(encoding);

            {
                (byte? r1, byte? r2) = await doublePrx.OpByteAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpByteAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (bool? r1, bool? r2) = await doublePrx.OpBoolAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpBoolAsync(true);
                Assert.That(r1, Is.True);
                Assert.That(r2, Is.True);
            }

            {
                (short? r1, short? r2) = await doublePrx.OpShortAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpShortAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (int? r1, int? r2) = await doublePrx.OpIntAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpIntAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (long? r1, long? r2) = await doublePrx.OpLongAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpLongAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (float? r1, float? r2) = await doublePrx.OpFloatAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpFloatAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (double? r1, double? r2) = await doublePrx.OpDoubleAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpDoubleAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (string? r1, string? r2) = await doublePrx.OpStringAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpStringAsync("hello");
                Assert.AreEqual("hello", r1);
                Assert.AreEqual("hello", r2);
            }

            {
                (MyEnum? r1, MyEnum? r2) = await doublePrx.OpMyEnumAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpMyEnumAsync(MyEnum.enum1);
                Assert.AreEqual(MyEnum.enum1, r1);
                Assert.AreEqual(MyEnum.enum1, r2);
            }

            {
                (MyStruct? r1, MyStruct? r2) = await doublePrx.OpMyStructAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyStruct(1, 1);
                (r1, r2) = await doublePrx.OpMyStructAsync(p1);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p1, r2);
            }

            {
                (AnotherStruct? r1, AnotherStruct? r2) = await doublePrx.OpAnotherStructAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new AnotherStruct(
                    "hello",
                    OperationsPrx.Parse("ice+tcp://localhost/hello"),
                    MyEnum.enum1,
                    new MyStruct(1, 1));
                (r1, r2) = await doublePrx.OpAnotherStructAsync(p1);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p1, r2);
            }

            {
                (byte[]? r1, byte[]? r2) = await doublePrx.OpByteSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                byte[] p1 = new byte[] { 42 };
                (r1, r2) = await doublePrx.OpByteSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<byte>? r1, List<byte>? r2) = await doublePrx.OpByteListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<byte> { 42 };
                (r1, r2) = await doublePrx.OpByteListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (byte?[]? r1, byte?[]? r2) = await doublePrx.OpOptionalByteSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                byte?[] p1 = new byte?[] { 42, null, 43 };
                (r1, r2) = await doublePrx.OpOptionalByteSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (bool[]? r1, bool[]? r2) = await doublePrx.OpBoolSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                bool[] p1 = new bool[] { true };
                (r1, r2) = await doublePrx.OpBoolSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<bool>? r1, List<bool>? r2) = await doublePrx.OpBoolListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<bool> { true };
                (r1, r2) = await doublePrx.OpBoolListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (bool?[]? r1, bool?[]? r2) = await doublePrx.OpOptionalBoolSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                bool?[] p1 = new bool?[] { true, null, false };
                (r1, r2) = await doublePrx.OpOptionalBoolSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (short[]? r1, short[]? r2) = await doublePrx.OpShortSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                short[] p1 = new short[] { 42 };
                (r1, r2) = await doublePrx.OpShortSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<short>? r1, List<short>? r2) = await doublePrx.OpShortListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<short> { 42 };
                (r1, r2) = await doublePrx.OpShortListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (short?[]? r1, short?[]? r2) = await doublePrx.OpOptionalShortSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                short?[] p1 = new short?[] { 42, null, 34 };
                (r1, r2) = await doublePrx.OpOptionalShortSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (int[]? r1, int[]? r2) = await doublePrx.OpIntSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                int[] p1 = new int[] { 42 };
                (r1, r2) = await doublePrx.OpIntSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<int>? r1, List<int>? r2) = await doublePrx.OpIntListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<int> { 42 };
                (r1, r2) = await doublePrx.OpIntListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (int?[]? r1, int?[]? r2) = await doublePrx.OpOptionalIntSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                int?[]? p1 = new int?[] { 42, null, 43 };
                (r1, r2) = await doublePrx.OpOptionalIntSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (long[]? r1, long[]? r2) = await doublePrx.OpLongSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                long[] p1 = new long[] { 42 };
                (r1, r2) = await doublePrx.OpLongSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<long>? r1, List<long>? r2) = await doublePrx.OpLongListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<long> { 42 };
                (r1, r2) = await doublePrx.OpLongListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (long?[]? r1, long?[]? r2) = await doublePrx.OpOptionalLongSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                long?[] p1 = new long?[] { 42, null, 43 };
                (r1, r2) = await doublePrx.OpOptionalLongSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (float[]? r1, float[]? r2) = await doublePrx.OpFloatSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                float[] p1 = new float[] { 42 };
                (r1, r2) = await doublePrx.OpFloatSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<float>? r1, List<float>? r2) = await doublePrx.OpFloatListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<float> { 42 };
                (r1, r2) = await doublePrx.OpFloatListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (float?[]? r1, float?[]? r2) = await doublePrx.OpOptionalFloatSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                float?[] p1 = new float?[] { 42, null, 43 };
                (r1, r2) = await doublePrx.OpOptionalFloatSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (double[]? r1, double[]? r2) = await doublePrx.OpDoubleSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                double[] p1 = new double[] { 42 };
                (r1, r2) = await doublePrx.OpDoubleSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<double>? r1, List<double>? r2) = await doublePrx.OpDoubleListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<double> { 42 };
                (r1, r2) = await doublePrx.OpDoubleListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (double?[]? r1, double?[]? r2) = await doublePrx.OpOptionalDoubleSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                double?[] p1 = new double?[] { 42 };
                (r1, r2) = await doublePrx.OpOptionalDoubleSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (string[]? r1, string[]? r2) = await doublePrx.OpStringSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                string[] p1 = new string[] { "hello" };
                (r1, r2) = await doublePrx.OpStringSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<string>? r1, List<string>? r2) = await doublePrx.OpStringListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<string> { "hello" };
                (r1, r2) = await doublePrx.OpStringListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (string?[]? r1, string?[]? r2) = await doublePrx.OpOptionalStringSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                string?[] p1 = new string?[] { "hello" };
                (r1, r2) = await doublePrx.OpOptionalStringSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (MyStruct[]? r1, MyStruct[]? r2) = await doublePrx.OpMyStructSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyStruct[] { new MyStruct(1, 1) };
                (r1, r2) = await doublePrx.OpMyStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<MyStruct>? r1, List<MyStruct>? r2) = await doublePrx.OpMyStructListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<MyStruct> { new MyStruct(1, 1) };
                (r1, r2) = await doublePrx.OpMyStructListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (MyStruct?[]? r1, MyStruct?[]? r2) = await doublePrx.OpOptionalMyStructSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyStruct?[] { new MyStruct(1, 1), null, new MyStruct(1, 1) };
                (r1, r2) = await doublePrx.OpOptionalMyStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (AnotherStruct[]? r1, AnotherStruct[]? r2) = await doublePrx.OpAnotherStructSeqAsync(null);
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
                (r1, r2) = await doublePrx.OpAnotherStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<AnotherStruct>? r1, List<AnotherStruct>? r2) = await doublePrx.OpAnotherStructListAsync(null);
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
                (r1, r2) = await doublePrx.OpAnotherStructListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (AnotherStruct?[]? r1, AnotherStruct?[]? r2) = await doublePrx.OpOptionalAnotherStructSeqAsync(null);
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
                (r1, r2) = await doublePrx.OpOptionalAnotherStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (Dictionary<int, int>? r1, Dictionary<int, int>? r2) = await doublePrx.OpIntDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<int, int> { { 1, 1 } };
                (r1, r2) = await doublePrx.OpIntDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (SortedDictionary<int, int>? r1, SortedDictionary<int, int>? r2) = await doublePrx.OpIntSortedDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new SortedDictionary<int, int> { { 1, 1 } };
                (r1, r2) = await doublePrx.OpIntSortedDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (Dictionary<int, int?>? r1, Dictionary<int, int?>? r2) = await doublePrx.OpOptionalIntDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<int, int?> { { 1, 1 } };
                (r1, r2) = await doublePrx.OpOptionalIntDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (SortedDictionary<int, int?>? r1, SortedDictionary<int, int?>? r2) =
                    await doublePrx.OpOptionalIntSortedDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new SortedDictionary<int, int?> { { 1, 1 } };
                (r1, r2) = await doublePrx.OpOptionalIntSortedDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (Dictionary<string, string>? r1, Dictionary<string, string>? r2) = await doublePrx.OpStringDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<string, string> { { "a", "b" } };
                (r1, r2) = await doublePrx.OpStringDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (SortedDictionary<string, string>? r1, SortedDictionary<string, string>? r2) =
                    await doublePrx.OpStringSortedDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new SortedDictionary<string, string> { { "a", "b" } };
                (r1, r2) = await doublePrx.OpStringSortedDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (Dictionary<string, string?>? r1, Dictionary<string, string?>? r2) =
                    await doublePrx.OpOptionalStringDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<string, string?> { { "a", "b" } };
                (r1, r2) = await doublePrx.OpOptionalStringDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (SortedDictionary<string, string?>? r1, SortedDictionary<string, string?>? r2) =
                    await doublePrx.OpOptionalStringSortedDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new SortedDictionary<string, string?> { { "a", "b" } };
                (r1, r2) = await doublePrx.OpOptionalStringSortedDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }
        }

        [TestCase("1.1")]
        [TestCase("2.0")]
        public async Task OperationTag_EncodedResult(string encoding)
        {
            var encodedResultPrx = OperationTagEncodedResultPrx.FromConnection(_connection);
            encodedResultPrx.Proxy.Encoding = Encoding.FromString(encoding);

            {
                MyStruct? r1 = await encodedResultPrx.OpMyStructAsync(null);
                Assert.That(r1, Is.Null);

                var p1 = new MyStruct(1, 1);
                r1 = await encodedResultPrx.OpMyStructAsync(p1);
                Assert.AreEqual(p1, r1);
            }

            {
                Dictionary<int, int>? r1 = await encodedResultPrx.OpIntDictAsync(null);
                Assert.That(r1, Is.Null);

                var p1 = new Dictionary<int, int> { { 1, 1 } };
                r1 = await encodedResultPrx.OpIntDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
            }

            {
                string[]? r1 = await encodedResultPrx.OpStringSeqAsync(null);
                Assert.That(r1, Is.Null);

                string[] p1 = new string[] { "hello" };
                r1 = await encodedResultPrx.OpStringSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
            }
        }

        [TestCase("1.1")]
        [TestCase("2.0")]
        public async Task OperationTag_DuplicateTag(string encoding)
        {
            OperationTagPrx prx = GetPrx(encoding);

            // Build a request payload with 2 tagged values
            ReadOnlyMemory<ReadOnlyMemory<byte>> requestPayload =
                prx.Proxy.GetIceEncoding().CreatePayloadFromArgs(
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
                await prx.Proxy.InvokeAsync("opVoid", prx.Proxy.Encoding, requestPayload);

            Assert.DoesNotThrow(() => response.CheckVoidReturnValue(
                prx.Proxy.Invoker,
                response.GetIceDecoderFactory(new DefaultIceDecoderFactories(typeof(OperationTagTests).Assembly))));
        }

        [TestCase("1.1")]
        [TestCase("2.0")]
        public async Task OperationTag_MinusTag(string encoding)
        {
            OperationTagPrx prx = GetPrx(encoding);

            // We use a compatible interface with fewer tags:
            var minusPrx = new OperationTagMinusPrx(prx.Proxy);
            int? r1 = await minusPrx.OpIntAsync();
            Assert.That(r1, Is.Null);
        }

        [TestCase("1.1")]
        [TestCase("2.0")]
        public async Task OperationTag_PlusTag(string encoding)
        {
            OperationTagPrx prx = GetPrx(encoding);

            // We use a compatible interface with more tags:
            var plusPrx = new OperationTagPlusPrx(prx.Proxy);

            {
                (int? r1, string? r2) = await plusPrx.OpIntAsync(42, "42");
                Assert.AreEqual(42, r1);
                Assert.That(r2, Is.Null);
            }

            {
                string? r1 = await plusPrx.OpVoidAsync("42");
                Assert.That(r1, Is.Null);
            }
        }

        private OperationTagPrx GetPrx(string encoding)
        {
            var prx = OperationTagPrx.FromConnection(_connection);
            prx.Proxy.Encoding = Encoding.FromString(encoding);
            return prx;
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

    public class OperationTagEncodedResult : Service, IOperationTagEncodedResult
    {
        public ValueTask<IOperationTagEncodedResult.OpIntDictEncodedResult> OpIntDictAsync(
            Dictionary<int, int>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(new IOperationTagEncodedResult.OpIntDictEncodedResult(p1, dispatch));

        public ValueTask<IOperationTagEncodedResult.OpStringSeqEncodedResult> OpStringSeqAsync(
            string[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(new IOperationTagEncodedResult.OpStringSeqEncodedResult(p1, dispatch));

        public ValueTask<IOperationTagEncodedResult.OpMyStructEncodedResult> OpMyStructAsync(
            MyStruct? p1,
            Dispatch dispatch,
            CancellationToken cancel) =>
            new(new IOperationTagEncodedResult.OpMyStructEncodedResult(p1, dispatch));
    }

    public class OperationTag : Service, IOperationTag
    {
        public ValueTask OpVoidAsync(Dispatch dispatch, CancellationToken cancel) => default;

        public ValueTask<int?> OpIntAsync(int? p1, Dispatch dispatch, CancellationToken cancel) => new(p1);
    }
}
