// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.IO.Pipelines;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("1.1")]
    [TestFixture("2.0")]
    public sealed class OperationTagTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly OperationTagPrx _prx;

        public OperationTagTests(string encoding)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher>(_ =>
                {
                    var router = new Router();
                    router.Map<IOperationTagDouble>(new OperationTagDouble());
                    router.Map<IOperationTagEncodedResult>(new OperationTagEncodedResult());
                    router.Map<IOperationTag>(new OperationTag());
                    return router;
                })
                .BuildServiceProvider();

            _prx = OperationTagPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
            _prx.Proxy.Encoding = Encoding.FromString(encoding);
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task OperationTag_Double()
        {
            var doublePrx = OperationTagDoublePrx.FromConnection(_prx.Proxy.Connection!);
            doublePrx.Proxy.Encoding = _prx.Proxy.Encoding;

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
                (MyCompactStruct? r1, MyCompactStruct? r2) = await doublePrx.OpMyCompactStructAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyCompactStruct(1, 1);
                (r1, r2) = await doublePrx.OpMyCompactStructAsync(p1);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p1, r2);
            }

            {
                (AnotherCompactStruct? r1, AnotherCompactStruct? r2) = await doublePrx.OpAnotherCompactStructAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new AnotherCompactStruct(
                    "hello",
                    OperationsPrx.Parse("icerpc://localhost/hello"),
                    MyEnum.enum1,
                    new MyCompactStruct(1, 1));
                (r1, r2) = await doublePrx.OpAnotherCompactStructAsync(p1);
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
                (MyCompactStruct[]? r1, MyCompactStruct[]? r2) = await doublePrx.OpMyCompactStructSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyCompactStruct[] { new MyCompactStruct(1, 1) };
                (r1, r2) = await doublePrx.OpMyCompactStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<MyCompactStruct>? r1, List<MyCompactStruct>? r2) = await doublePrx.OpMyCompactStructListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<MyCompactStruct> { new MyCompactStruct(1, 1) };
                (r1, r2) = await doublePrx.OpMyCompactStructListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (MyCompactStruct?[]? r1, MyCompactStruct?[]? r2) = await doublePrx.OpOptionalMyCompactStructSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyCompactStruct?[] { new MyCompactStruct(1, 1), null, new MyCompactStruct(1, 1) };
                (r1, r2) = await doublePrx.OpOptionalMyCompactStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (AnotherCompactStruct[]? r1, AnotherCompactStruct[]? r2) = await doublePrx.OpAnotherCompactStructSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new AnotherCompactStruct[]
                {
                    new AnotherCompactStruct(
                        "hello",
                        OperationsPrx.Parse("icerpc://localhost/hello"),
                        MyEnum.enum1,
                        new MyCompactStruct(1, 1))
                };
                (r1, r2) = await doublePrx.OpAnotherCompactStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<AnotherCompactStruct>? r1, List<AnotherCompactStruct>? r2) = await doublePrx.OpAnotherCompactStructListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<AnotherCompactStruct>
                {
                    new AnotherCompactStruct(
                        "hello",
                        OperationsPrx.Parse("icerpc://localhost/hello"),
                        MyEnum.enum1,
                        new MyCompactStruct(1, 1))
                };
                (r1, r2) = await doublePrx.OpAnotherCompactStructListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (AnotherCompactStruct?[]? r1, AnotherCompactStruct?[]? r2) = await doublePrx.OpOptionalAnotherCompactStructSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new AnotherCompactStruct?[]
                {
                    new AnotherCompactStruct(
                        "hello",
                        OperationsPrx.Parse("icerpc://localhost/hello"),
                        MyEnum.enum1,
                        new MyCompactStruct(1, 1)),
                    null,
                    new AnotherCompactStruct(
                        "hello",
                        OperationsPrx.Parse("icerpc://localhost/hello"),
                        MyEnum.enum1,
                        new MyCompactStruct(1, 1)),

                };
                (r1, r2) = await doublePrx.OpOptionalAnotherCompactStructSeqAsync(p1);
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
                (CustomDictionary<int, int>? r1, CustomDictionary<int, int>? r2) = await doublePrx.OpIntCustomDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new CustomDictionary<int, int> { { 1, 1 } };
                (r1, r2) = await doublePrx.OpIntCustomDictAsync(p1);
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
                (CustomDictionary<int, int?>? r1, CustomDictionary<int, int?>? r2) =
                    await doublePrx.OpOptionalIntCustomDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new CustomDictionary<int, int?> { { 1, 1 } };
                (r1, r2) = await doublePrx.OpOptionalIntCustomDictAsync(p1);
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
                (CustomDictionary<string, string>? r1, CustomDictionary<string, string>? r2) =
                    await doublePrx.OpStringCustomDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new CustomDictionary<string, string> { { "a", "b" } };
                (r1, r2) = await doublePrx.OpStringCustomDictAsync(p1);
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
                (CustomDictionary<string, string?>? r1, CustomDictionary<string, string?>? r2) =
                    await doublePrx.OpOptionalStringCustomDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new CustomDictionary<string, string?> { { "a", "b" } };
                (r1, r2) = await doublePrx.OpOptionalStringCustomDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }
        }

        [Test]
        public async Task OperationTag_EncodedResult()
        {
            var encodedResultPrx = OperationTagEncodedResultPrx.FromConnection(_prx.Proxy.Connection!);
            encodedResultPrx.Proxy.Encoding = _prx.Proxy.Encoding;

            {
                MyCompactStruct? r1 = await encodedResultPrx.OpMyCompactStructAsync(null);
                Assert.That(r1, Is.Null);

                var p1 = new MyCompactStruct(1, 1);
                r1 = await encodedResultPrx.OpMyCompactStructAsync(p1);
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

        [Test]
        public async Task OperationTag_DuplicateTag()
        {
            PipeReader requestPayload = CreatePayload();
            var request = new OutgoingRequest(_prx.Proxy, "opVoid")
            {
                PayloadEncoding = _prx.Proxy.Encoding,
                PayloadSource = requestPayload
            };

            IncomingResponse response = await _prx.Proxy.Invoker.InvokeAsync(request);

            Assert.DoesNotThrowAsync(async () => await response.CheckVoidReturnValueAsync(
                SliceDecoder.GetActivator(typeof(OperationTagTests).Assembly),
                hasStream: false,
                default));

            PipeReader CreatePayload()
            {
                // Build a request payload with 2 tagged values
                var pipe = new Pipe(); // TODO: pipe options
                SliceEncoding encoding = _prx.Proxy.GetSliceEncoding();
                var encoder = new SliceEncoder(pipe.Writer, encoding, default);
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(4);
                int startPos = encoder.EncodedByteCount;

                encoder.EncodeTagged(
                    1,
                    TagFormat.F4,
                    size: 4,
                    15,
                    (ref SliceEncoder encoder, int v) => encoder.EncodeInt(v));

                encoder.EncodeTagged(
                    1, // duplicate tag ignored by the server
                    TagFormat.OVSize,
                    "test",
                    (ref SliceEncoder encoder, string v) => encoder.EncodeString(v));

                encoding.EncodeFixedLengthSize(encoder.EncodedByteCount - startPos, sizePlaceholder);

                pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
                return pipe.Reader;
            }
        }

        [Test]
        public async Task OperationTag_MinusTag()
        {
            // We use a compatible interface with fewer tags:
            var minusPrx = new OperationTagMinusPrx(_prx.Proxy);
            int? r1 = await minusPrx.OpIntAsync();
            Assert.That(r1, Is.Null);
        }

        [Test]
        public async Task OperationTag_PlusTag()
        {
            // We use a compatible interface with more tags:
            var plusPrx = new OperationTagPlusPrx(_prx.Proxy);

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
    }

    public class OperationTagDouble : Service, IOperationTagDouble
    {
        public ValueTask<(AnotherCompactStruct? R1, AnotherCompactStruct? R2)> OpAnotherCompactStructAsync(
            AnotherCompactStruct? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<AnotherCompactStruct>? R1, IEnumerable<AnotherCompactStruct>? R2)> OpAnotherCompactStructListAsync(
            List<AnotherCompactStruct>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<AnotherCompactStruct>? R1, IEnumerable<AnotherCompactStruct>? R2)> OpAnotherCompactStructSeqAsync(
            AnotherCompactStruct[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<AnotherCompactStruct?>? R1, IEnumerable<AnotherCompactStruct?>? R2)> OpOptionalAnotherCompactStructSeqAsync(
            AnotherCompactStruct?[]? p1,
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

        public ValueTask<(IEnumerable<KeyValuePair<int, int>>? R1, IEnumerable<KeyValuePair<int, int>>? R2)> OpIntCustomDictAsync(
            CustomDictionary<int, int>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<int, int?>>? R1, IEnumerable<KeyValuePair<int, int?>>? R2)> OpOptionalIntDictAsync(
            Dictionary<int, int?>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<int, int?>>? R1, IEnumerable<KeyValuePair<int, int?>>? R2)> OpOptionalIntCustomDictAsync(
            CustomDictionary<int, int?>? p1,
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

        public ValueTask<(MyCompactStruct? R1, MyCompactStruct? R2)> OpMyCompactStructAsync(
            MyCompactStruct? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyCompactStruct>? R1, IEnumerable<MyCompactStruct>? R2)> OpMyCompactStructListAsync(
            List<MyCompactStruct>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyCompactStruct>? R1, IEnumerable<MyCompactStruct>? R2)> OpMyCompactStructSeqAsync(
            MyCompactStruct[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyCompactStruct?>? R1, IEnumerable<MyCompactStruct?>? R2)> OpOptionalMyCompactStructSeqAsync(
            MyCompactStruct?[]? p1,
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

        public ValueTask<(IEnumerable<KeyValuePair<string, string>>? R1, IEnumerable<KeyValuePair<string, string>>? R2)> OpStringCustomDictAsync(
            CustomDictionary<string, string>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<string, string?>>? R1, IEnumerable<KeyValuePair<string, string?>>? R2)> OpOptionalStringDictAsync(
            Dictionary<string, string?>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<string, string?>>? R1, IEnumerable<KeyValuePair<string, string?>>? R2)> OpOptionalStringCustomDictAsync(
            CustomDictionary<string, string?>? p1,
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

        public ValueTask<IOperationTagEncodedResult.OpMyCompactStructEncodedResult> OpMyCompactStructAsync(
            MyCompactStruct? p1,
            Dispatch dispatch,
            CancellationToken cancel) =>
            new(new IOperationTagEncodedResult.OpMyCompactStructEncodedResult(p1, dispatch));
    }

    public class OperationTag : Service, IOperationTag
    {
        public ValueTask OpVoidAsync(Dispatch dispatch, CancellationToken cancel) => default;

        public ValueTask<int?> OpIntAsync(int? p1, Dispatch dispatch, CancellationToken cancel) => new(p1);
    }
}
