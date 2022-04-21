// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
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
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task OperationTag_Double()
        {
            var doublePrx = OperationTagDoublePrx.FromConnection(_prx.Proxy.Connection!);

            {
                (byte? r1, byte? r2) = await doublePrx.OpUInt8Async(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpUInt8Async(42);
                Assert.That(r1, Is.EqualTo(42));
                Assert.That(r2, Is.EqualTo(42));
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
                (short? r1, short? r2) = await doublePrx.OpInt16Async(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpInt16Async(42);
                Assert.That(r1, Is.EqualTo(42));
                Assert.That(r2, Is.EqualTo(42));
            }

            {
                (int? r1, int? r2) = await doublePrx.OpInt32Async(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpInt32Async(42);
                Assert.That(r1, Is.EqualTo(42));
                Assert.That(r2, Is.EqualTo(42));
            }

            {
                (long? r1, long? r2) = await doublePrx.OpInt64Async(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpInt64Async(42);
                Assert.That(r1, Is.EqualTo(42));
                Assert.That(r2, Is.EqualTo(42));
            }

            {
                (float? r1, float? r2) = await doublePrx.OpFloat32Async(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpFloat32Async(42);
                Assert.That(r1, Is.EqualTo(42));
                Assert.That(r2, Is.EqualTo(42));
            }

            {
                (double? r1, double? r2) = await doublePrx.OpFloat64Async(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpFloat64Async(42);
                Assert.That(r1, Is.EqualTo(42));
                Assert.That(r2, Is.EqualTo(42));
            }

            {
                (string? r1, string? r2) = await doublePrx.OpStringAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpStringAsync("hello");
                Assert.That(r1, Is.EqualTo("hello"));
                Assert.That(r2, Is.EqualTo("hello"));
            }

            {
                (MyEnum? r1, MyEnum? r2) = await doublePrx.OpMyEnumAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await doublePrx.OpMyEnumAsync(MyEnum.enum1);
                Assert.That(r1, Is.EqualTo(MyEnum.enum1));
                Assert.That(r2, Is.EqualTo(MyEnum.enum1));
            }

            {
                (MyCompactStruct? r1, MyCompactStruct? r2) = await doublePrx.OpMyCompactStructAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyCompactStruct(1, 1);
                (r1, r2) = await doublePrx.OpMyCompactStructAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
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
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (byte[]? r1, byte[]? r2) = await doublePrx.OpUInt8SeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                byte[] p1 = new byte[] { 42 };
                (r1, r2) = await doublePrx.OpUInt8SeqAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (List<byte>? r1, List<byte>? r2) = await doublePrx.OpUInt8ListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<byte> { 42 };
                (r1, r2) = await doublePrx.OpUInt8ListAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (bool[]? r1, bool[]? r2) = await doublePrx.OpBoolSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                bool[] p1 = new bool[] { true };
                (r1, r2) = await doublePrx.OpBoolSeqAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (List<bool>? r1, List<bool>? r2) = await doublePrx.OpBoolListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<bool> { true };
                (r1, r2) = await doublePrx.OpBoolListAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (short[]? r1, short[]? r2) = await doublePrx.OpInt16SeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                short[] p1 = new short[] { 42 };
                (r1, r2) = await doublePrx.OpInt16SeqAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (List<short>? r1, List<short>? r2) = await doublePrx.OpInt16ListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<short> { 42 };
                (r1, r2) = await doublePrx.OpInt16ListAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (int[]? r1, int[]? r2) = await doublePrx.OpInt32SeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                int[] p1 = new int[] { 42 };
                (r1, r2) = await doublePrx.OpInt32SeqAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (List<int>? r1, List<int>? r2) = await doublePrx.OpInt32ListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<int> { 42 };
                (r1, r2) = await doublePrx.OpInt32ListAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (long[]? r1, long[]? r2) = await doublePrx.OpInt64SeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                long[] p1 = new long[] { 42 };
                (r1, r2) = await doublePrx.OpInt64SeqAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (List<long>? r1, List<long>? r2) = await doublePrx.OpInt64ListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<long> { 42 };
                (r1, r2) = await doublePrx.OpInt64ListAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (float[]? r1, float[]? r2) = await doublePrx.OpFloat32SeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                float[] p1 = new float[] { 42 };
                (r1, r2) = await doublePrx.OpFloat32SeqAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (List<float>? r1, List<float>? r2) = await doublePrx.OpFloat32ListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<float> { 42 };
                (r1, r2) = await doublePrx.OpFloat32ListAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (double[]? r1, double[]? r2) = await doublePrx.OpFloat64SeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                double[] p1 = new double[] { 42 };
                (r1, r2) = await doublePrx.OpFloat64SeqAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (List<double>? r1, List<double>? r2) = await doublePrx.OpFloat64ListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<double> { 42 };
                (r1, r2) = await doublePrx.OpFloat64ListAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (string[]? r1, string[]? r2) = await doublePrx.OpStringSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                string[] p1 = new string[] { "hello" };
                (r1, r2) = await doublePrx.OpStringSeqAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (List<string>? r1, List<string>? r2) = await doublePrx.OpStringListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<string> { "hello" };
                (r1, r2) = await doublePrx.OpStringListAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (MyCompactStruct[]? r1, MyCompactStruct[]? r2) = await doublePrx.OpMyCompactStructSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyCompactStruct[] { new MyCompactStruct(1, 1) };
                (r1, r2) = await doublePrx.OpMyCompactStructSeqAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (List<MyCompactStruct>? r1, List<MyCompactStruct>? r2) = await doublePrx.OpMyCompactStructListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<MyCompactStruct> { new MyCompactStruct(1, 1) };
                (r1, r2) = await doublePrx.OpMyCompactStructListAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
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
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
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
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (Dictionary<int, int>? r1, Dictionary<int, int>? r2) = await doublePrx.OpInt32DictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<int, int> { { 1, 1 } };
                (r1, r2) = await doublePrx.OpInt32DictAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (CustomDictionary<int, int>? r1, CustomDictionary<int, int>? r2) = await doublePrx.OpInt32CustomDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new CustomDictionary<int, int> { { 1, 1 } };
                (r1, r2) = await doublePrx.OpInt32CustomDictAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (Dictionary<string, string>? r1, Dictionary<string, string>? r2) = await doublePrx.OpStringDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<string, string> { { "a", "b" } };
                (r1, r2) = await doublePrx.OpStringDictAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }

            {
                (CustomDictionary<string, string>? r1, CustomDictionary<string, string>? r2) =
                    await doublePrx.OpStringCustomDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new CustomDictionary<string, string> { { "a", "b" } };
                (r1, r2) = await doublePrx.OpStringCustomDictAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
                Assert.That(r2, Is.EqualTo(p1));
            }
        }

        [Test]
        public async Task OperationTag_EncodedResult()
        {
            var encodedResultPrx = OperationTagEncodedResultPrx.FromConnection(_prx.Proxy.Connection!);

            {
                MyCompactStruct? r1 = await encodedResultPrx.OpMyCompactStructAsync(null);
                Assert.That(r1, Is.Null);

                var p1 = new MyCompactStruct(1, 1);
                r1 = await encodedResultPrx.OpMyCompactStructAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
            }

            {
                Dictionary<int, int>? r1 = await encodedResultPrx.OpInt32DictAsync(null);
                Assert.That(r1, Is.Null);

                var p1 = new Dictionary<int, int> { { 1, 1 } };
                r1 = await encodedResultPrx.OpInt32DictAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
            }

            {
                string[]? r1 = await encodedResultPrx.OpStringSeqAsync(null);
                Assert.That(r1, Is.Null);

                string[] p1 = new string[] { "hello" };
                r1 = await encodedResultPrx.OpStringSeqAsync(p1);
                Assert.That(r1, Is.EqualTo(p1));
            }
        }

        [Test]
        public async Task OperationTag_DuplicateTag()
        {
            PipeReader requestPayload = CreatePayload();
            var request = new OutgoingRequest(_prx.Proxy)
            {
                Operation = "opVoid",
                Payload = requestPayload
            };

            IncomingResponse response = await _prx.Proxy.Invoker.InvokeAsync(request);

            Assert.DoesNotThrowAsync(async () => await response.DecodeVoidReturnValueAsync(
                SliceEncoding.Slice2,
                SliceDecoder.GetActivator(typeof(OperationTagTests).Assembly),
                hasStream: false,
                default));

            static PipeReader CreatePayload()
            {
                // Build a request payload with 2 tagged values
                var pipe = new Pipe(); // TODO: pipe options

                var encoder = new SliceEncoder(pipe.Writer, SliceEncoding.Slice2, default);
                Span<byte> sizePlaceholder = encoder.GetPlaceholderSpan(2);
                int startPos = encoder.EncodedByteCount;

                encoder.EncodeTagged(
                    1,
                    size: 4,
                    15,
                    (ref SliceEncoder encoder, int v) => encoder.EncodeInt32(v));

                encoder.EncodeTagged(
                    1, // duplicate tag ignored by the server
                    "test",
                    (ref SliceEncoder encoder, string v) => encoder.EncodeString(v));

                SliceEncoder.EncodeVarUInt62((ulong)(encoder.EncodedByteCount - startPos), sizePlaceholder);

                pipe.Writer.Complete();  // flush to reader and sets Is[Writer]Completed to true.
                return pipe.Reader;
            }
        }

        [Test]
        public async Task OperationTag_MinusTag()
        {
            // We use a compatible interface with fewer tags:
            var minusPrx = new OperationTagMinusPrx(_prx.Proxy);
            int? r1 = await minusPrx.OpInt32Async();
            Assert.That(r1, Is.Null);
        }

        [Test]
        public async Task OperationTag_PlusTag()
        {
            // We use a compatible interface with more tags:
            var plusPrx = new OperationTagPlusPrx(_prx.Proxy);

            {
                (int? r1, string? r2) = await plusPrx.OpInt32Async(42, "42");
                Assert.That(r1, Is.EqualTo(42));
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

        public ValueTask<(byte? R1, byte? R2)> OpUInt8Async(
            byte? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<byte>? R1, IEnumerable<byte>? R2)> OpUInt8ListAsync(
            List<byte>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<byte> R1, ReadOnlyMemory<byte> R2)> OpUInt8SeqAsync(
            byte[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(double? R1, double? R2)> OpFloat64Async(
            double? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<double>? R1, IEnumerable<double>? R2)> OpFloat64ListAsync(
            List<double>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<double> R1, ReadOnlyMemory<double> R2)> OpFloat64SeqAsync(
            double[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(float? R1, float? R2)> OpFloat32Async(
            float? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<float>? R1, IEnumerable<float>? R2)> OpFloat32ListAsync(
            List<float>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<float> R1, ReadOnlyMemory<float> R2)> OpFloat32SeqAsync(
            float[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(int? R1, int? R2)> OpInt32Async(
            int? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<int, int>>? R1, IEnumerable<KeyValuePair<int, int>>? R2)> OpInt32DictAsync(
            Dictionary<int, int>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<KeyValuePair<int, int>>? R1, IEnumerable<KeyValuePair<int, int>>? R2)> OpInt32CustomDictAsync(
            CustomDictionary<int, int>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<int>? R1, IEnumerable<int>? R2)> OpInt32ListAsync(
            List<int>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<int> R1, ReadOnlyMemory<int> R2)> OpInt32SeqAsync(
            int[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(long? R1, long? R2)> OpInt64Async(
            long? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<long>? R1, IEnumerable<long>? R2)> OpInt64ListAsync(
            List<long>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<long> R1, ReadOnlyMemory<long> R2)> OpInt64SeqAsync(
            long[]? p1,
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

        public ValueTask<(short? R1, short? R2)> OpInt16Async(
            short? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<short>? R1, IEnumerable<short>? R2)> OpInt16ListAsync(
            List<short>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<short> R1, ReadOnlyMemory<short> R2)> OpInt16SeqAsync(
            short[]? p1,
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

        public ValueTask<(IEnumerable<string>? R1, IEnumerable<string>? R2)> OpStringListAsync(
            List<string>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<string>? R1, IEnumerable<string>? R2)> OpStringSeqAsync(
            string[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyEnum>? R1, IEnumerable<MyEnum>? R2)> OpMyEnumSeqAsync(
            MyEnum[]? p1,
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
    }

    public class OperationTagEncodedResult : Service, IOperationTagEncodedResult
    {
        public ValueTask<IOperationTagEncodedResult.OpInt32DictEncodedResult> OpInt32DictAsync(
            Dictionary<int, int>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(new IOperationTagEncodedResult.OpInt32DictEncodedResult(p1));

        public ValueTask<IOperationTagEncodedResult.OpStringSeqEncodedResult> OpStringSeqAsync(
            string[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(new IOperationTagEncodedResult.OpStringSeqEncodedResult(p1));

        public ValueTask<IOperationTagEncodedResult.OpMyCompactStructEncodedResult> OpMyCompactStructAsync(
            MyCompactStruct? p1,
            Dispatch dispatch,
            CancellationToken cancel) =>
            new(new IOperationTagEncodedResult.OpMyCompactStructEncodedResult(p1));
    }

    public class OperationTag : Service, IOperationTag
    {
        public ValueTask OpVoidAsync(Dispatch dispatch, CancellationToken cancel) => default;

        public ValueTask<int?> OpInt32Async(int? p1, Dispatch dispatch, CancellationToken cancel) => new(p1);
    }
}
