// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using NUnit.Framework;

namespace IceRpc.Tests.CodeGeneration
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public sealed class TaggedTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly TaggedOperationsPrx _prx;

        public TaggedTests()
        {
            _server = new Server
            {
                Dispatcher = new TaggedOperations(),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            _server.Listen();
            _connection = new Connection
            {
                RemoteEndpoint = _server.Endpoint
            };
            _prx = TaggedOperationsPrx.FromConnection(_connection);
        }

        [OneTimeTearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Test]
        public void Tagged_DataMembers()
        {
            var oneTagged = new OneTagged();
            Assert.That(oneTagged.A.HasValue, Is.False);

            oneTagged = new OneTagged(16);
            Assert.AreEqual(16, oneTagged.A);

            CheckMultiTaggedHasNoValue(new MultiTagged());
        }

        [Test]
        public async Task Tagged_Parameters()
        {
            var oneTagged = (OneTagged)await _prx.PingPongAsync(new OneTagged());
            Assert.That(oneTagged.A.HasValue, Is.False);

            oneTagged = (OneTagged)await _prx.PingPongAsync(new OneTagged(16));
            Assert.AreEqual(16, oneTagged.A);

            var multiTagged = (MultiTagged)await _prx.PingPongAsync(new MultiTagged());
            CheckMultiTaggedHasNoValue(multiTagged);

            multiTagged.MByte = 1;
            multiTagged.MShort = 1;
            multiTagged.MLong = 1;
            multiTagged.MDouble = 1.0;
            multiTagged.MUShort = 1;
            multiTagged.MULong = 1;
            multiTagged.MVarLong = 1;
            multiTagged.MString = "1";
            multiTagged.MMyEnum = MyEnum.enum1;
            multiTagged.MAnotherStruct = new AnotherStruct(
                "hello",
                OperationsPrx.Parse("ice+tcp://localhost/hello"),
                MyEnum.enum1,
                new MyStruct(1, 1));

            multiTagged.MStringSeq = new string[] { "hello" };
            multiTagged.MMyEnumSeq = new MyEnum[] { MyEnum.enum1 };
            multiTagged.MAnotherStructSeq = new AnotherStruct[] { multiTagged.MAnotherStruct.Value };

            multiTagged.MStringDict = new Dictionary<string, string>()
            {
                { "key", "value" }
            };
            multiTagged.MVarIntSeq = new int[] { 1 };

            multiTagged.MByteDict = new Dictionary<byte, byte>() { { 1, 1 } };
            multiTagged.MAnotherStructDict = new Dictionary<string, AnotherStruct>()
            {
                { "key", multiTagged.MAnotherStruct.Value}
            };

            var multiTagged1 = (MultiTagged)await _prx.PingPongAsync(multiTagged);
            Assert.AreEqual(multiTagged.MByte, multiTagged1.MByte);
            Assert.AreEqual(multiTagged.MBool, multiTagged1.MBool);
            Assert.AreEqual(multiTagged.MShort, multiTagged1.MShort);
            Assert.AreEqual(multiTagged.MInt, multiTagged1.MInt);
            Assert.AreEqual(multiTagged.MLong, multiTagged1.MLong);
            Assert.AreEqual(multiTagged.MFloat, multiTagged1.MFloat);
            Assert.AreEqual(multiTagged.MDouble, multiTagged1.MDouble);
            Assert.AreEqual(multiTagged.MUShort, multiTagged1.MUShort);
            Assert.AreEqual(multiTagged.MUInt, multiTagged1.MUInt);
            Assert.AreEqual(multiTagged.MULong, multiTagged1.MULong);
            Assert.AreEqual(multiTagged.MVarInt, multiTagged1.MVarInt);
            Assert.AreEqual(multiTagged.MVarLong, multiTagged1.MVarLong);
            Assert.AreEqual(multiTagged.MVarUInt, multiTagged1.MVarUInt);
            Assert.AreEqual(multiTagged.MVarULong, multiTagged1.MVarULong);
            Assert.AreEqual(multiTagged.MString, multiTagged1.MString);
            Assert.AreEqual(multiTagged.MMyEnum, multiTagged1.MMyEnum);
            Assert.AreEqual(multiTagged.MMyStruct, multiTagged1.MMyStruct);
            Assert.AreEqual(multiTagged.MAnotherStruct, multiTagged1.MAnotherStruct);

            Assert.That(multiTagged1.MByteSeq, Is.Null);
            CollectionAssert.AreEqual(multiTagged.MStringSeq, multiTagged1.MStringSeq);
            Assert.That(multiTagged1.MShortSeq, Is.Null);
            CollectionAssert.AreEqual(multiTagged.MMyEnumSeq, multiTagged1.MMyEnumSeq);
            Assert.That(multiTagged1.MMyStructSeq, Is.Null);
            CollectionAssert.AreEqual(multiTagged.MAnotherStructSeq, multiTagged1.MAnotherStructSeq);

            Assert.That(multiTagged1.MIntDict, Is.Null);
            CollectionAssert.AreEqual(multiTagged.MStringDict, multiTagged1.MStringDict);
            Assert.That(multiTagged1.MUShortSeq, Is.Null);
            Assert.That(multiTagged1.MVarULongSeq, Is.Null);
            CollectionAssert.AreEqual(multiTagged.MVarIntSeq, multiTagged1.MVarIntSeq);

            CollectionAssert.AreEqual(multiTagged.MByteDict, multiTagged1.MByteDict);
            Assert.That(multiTagged1.MMyStructDict, Is.Null);
            CollectionAssert.AreEqual(multiTagged.MAnotherStructDict, multiTagged1.MAnotherStructDict);

            multiTagged = new MultiTagged();
            multiTagged.MBool = true;
            multiTagged.MInt = 1;
            multiTagged.MFloat = 1;
            multiTagged.MUShort = 1;
            multiTagged.MULong = 1;
            multiTagged.MVarLong = 1;
            multiTagged.MVarULong = 1;
            multiTagged.MMyEnum = MyEnum.enum1;
            multiTagged.MMyStruct = new MyStruct(1, 1);

            multiTagged.MByteSeq = new byte[] { 1 };
            multiTagged.MShortSeq = new short[] { 1 };
            multiTagged.MMyStructSeq = new MyStruct[] { new MyStruct(1, 1) };

            multiTagged.MIntDict = new Dictionary<int, int> { { 1, 1 } };
            multiTagged.MUShortSeq = new ushort[] { 1 };
            multiTagged.MVarIntSeq = new int[] { 1 };
            multiTagged.MMyStructDict = new Dictionary<MyStruct, MyStruct>()
            {
                { new MyStruct(1, 1), new MyStruct(1, 1) }
            };

            multiTagged1 = (MultiTagged)await _prx.PingPongAsync(multiTagged);
            Assert.AreEqual(multiTagged.MByte, multiTagged1.MByte);
            Assert.AreEqual(multiTagged.MBool, multiTagged1.MBool);
            Assert.AreEqual(multiTagged.MShort, multiTagged1.MShort);
            Assert.AreEqual(multiTagged.MInt, multiTagged1.MInt);
            Assert.AreEqual(multiTagged.MLong, multiTagged1.MLong);
            Assert.AreEqual(multiTagged.MFloat, multiTagged1.MFloat);
            Assert.AreEqual(multiTagged.MDouble, multiTagged1.MDouble);
            Assert.AreEqual(multiTagged.MUShort, multiTagged1.MUShort);
            Assert.AreEqual(multiTagged.MUInt, multiTagged1.MUInt);
            Assert.AreEqual(multiTagged.MULong, multiTagged1.MULong);
            Assert.AreEqual(multiTagged.MVarInt, multiTagged1.MVarInt);
            Assert.AreEqual(multiTagged.MVarLong, multiTagged1.MVarLong);
            Assert.AreEqual(multiTagged.MVarUInt, multiTagged1.MVarUInt);
            Assert.AreEqual(multiTagged.MVarULong, multiTagged1.MVarULong);
            Assert.AreEqual(multiTagged.MString, multiTagged1.MString);
            Assert.AreEqual(multiTagged.MMyEnum, multiTagged1.MMyEnum);
            Assert.AreEqual(multiTagged.MMyStruct, multiTagged1.MMyStruct);
            Assert.AreEqual(multiTagged.MAnotherStruct, multiTagged1.MAnotherStruct);

            CollectionAssert.AreEqual(multiTagged.MByteSeq, multiTagged1.MByteSeq);
            Assert.That(multiTagged1.MStringSeq, Is.Null);
            CollectionAssert.AreEqual(multiTagged.MShortSeq, multiTagged1.MShortSeq);
            Assert.That(multiTagged1.MMyEnumSeq, Is.Null);
            CollectionAssert.AreEqual(multiTagged.MMyStructSeq, multiTagged1.MMyStructSeq);
            Assert.That(multiTagged1.MAnotherStructSeq, Is.Null);

            CollectionAssert.AreEqual(multiTagged.MIntDict, multiTagged1.MIntDict);
            Assert.That(multiTagged1.MStringDict, Is.Null);
            CollectionAssert.AreEqual(multiTagged.MUShortSeq, multiTagged1.MUShortSeq);
            Assert.That(multiTagged1.MVarULongSeq, Is.Null);
            CollectionAssert.AreEqual(multiTagged.MVarIntSeq, multiTagged1.MVarIntSeq);

            Assert.That(multiTagged1.MByteDict, Is.Null);
            CollectionAssert.AreEqual(multiTagged.MMyStructDict, multiTagged1.MMyStructDict);
            Assert.That(multiTagged1.MAnotherStructDict, Is.Null);

            // Build a request payload with 2 tagged values
            ReadOnlyMemory<ReadOnlyMemory<byte>> requestPayload =
                _prx.Proxy.GetIceEncoding().CreatePayloadFromArgs(
                    (15, "test"),
                    (IceEncoder encoder, in (int? N, string? S) value) =>
                    {
                        encoder.EncodeTagged(1, TagFormat.F4, size: 4, value.N, (encoder, v) => encoder.EncodeInt(v!.Value));
                        encoder.EncodeTagged(1,
                                             TagFormat.VSize,
                                             value.S,
                                             (encoder, v) => encoder.EncodeString(v!)); // duplicate tag ignored by the server
                    });

            (IncomingResponse response, StreamParamReceiver? _) =
                await _prx.Proxy.InvokeAsync("opVoid", _prx.Proxy.Encoding, requestPayload);

            Assert.DoesNotThrow(() => response.CheckVoidReturnValue(
                _prx.Proxy.Invoker,
                new DefaultIceDecoderFactories(typeof(TaggedTests).Assembly)));

            var b = (B)await _prx.PingPongAsync(new B());
            Assert.That(b.MInt2.HasValue, Is.False);
            Assert.That(b.MInt3.HasValue, Is.False);
            Assert.That(b.MInt4.HasValue, Is.False);
            Assert.That(b.MInt6.HasValue, Is.False);

            b = (B)await _prx.PingPongAsync(new B(10, 11, 12, 13, 0, null));
            Assert.AreEqual(10, b.MInt1);
            Assert.AreEqual(11, b.MInt2);
            Assert.AreEqual(12, b.MInt3);
            Assert.AreEqual(13, b.MInt4);
            Assert.AreEqual(0, b.MInt5);
            Assert.That(b.MInt6.HasValue, Is.False);

            {
                (byte? r1, byte? r2) = await _prx.OpByteAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _prx.OpByteAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (bool? r1, bool? r2) = await _prx.OpBoolAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _prx.OpBoolAsync(true);
                Assert.That(r1, Is.True);
                Assert.That(r2, Is.True);
            }

            {
                (short? r1, short? r2) = await _prx.OpShortAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _prx.OpShortAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (int? r1, int? r2) = await _prx.OpIntAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _prx.OpIntAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (long? r1, long? r2) = await _prx.OpLongAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _prx.OpLongAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (float? r1, float? r2) = await _prx.OpFloatAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _prx.OpFloatAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (double? r1, double? r2) = await _prx.OpDoubleAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _prx.OpDoubleAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (string? r1, string? r2) = await _prx.OpStringAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _prx.OpStringAsync("hello");
                Assert.AreEqual("hello", r1);
                Assert.AreEqual("hello", r2);
            }

            {
                (MyEnum? r1, MyEnum? r2) = await _prx.OpMyEnumAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                (r1, r2) = await _prx.OpMyEnumAsync(MyEnum.enum1);
                Assert.AreEqual(MyEnum.enum1, r1);
                Assert.AreEqual(MyEnum.enum1, r2);
            }

            {
                (MyStruct? r1, MyStruct? r2) = await _prx.OpMyStructAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyStruct(1, 1);
                (r1, r2) = await _prx.OpMyStructAsync(p1);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p1, r2);
            }

            {
                MyStruct? r1 = await _prx.OpMyStructMarshaledResultAsync(null);
                Assert.That(r1, Is.Null);

                var p1 = new MyStruct(1, 1);
                r1 = await _prx.OpMyStructMarshaledResultAsync(p1);
                Assert.AreEqual(p1, r1);
            }

            {
                (AnotherStruct? r1, AnotherStruct? r2) = await _prx.OpAnotherStructAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new AnotherStruct(
                    "hello",
                    OperationsPrx.Parse("ice+tcp://localhost/hello"),
                    MyEnum.enum1,
                    new MyStruct(1, 1));
                (r1, r2) = await _prx.OpAnotherStructAsync(p1);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p1, r2);
            }

            {
                (byte[]? r1, byte[]? r2) = await _prx.OpByteSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                byte[] p1 = new byte[] { 42 };
                (r1, r2) = await _prx.OpByteSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<byte>? r1, List<byte>? r2) = await _prx.OpByteListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<byte> { 42 };
                (r1, r2) = await _prx.OpByteListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (byte?[]? r1, byte?[]? r2) = await _prx.OpOptionalByteSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                byte?[] p1 = new byte?[] { 42, null, 43 };
                (r1, r2) = await _prx.OpOptionalByteSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (bool[]? r1, bool[]? r2) = await _prx.OpBoolSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                bool[] p1 = new bool[] { true };
                (r1, r2) = await _prx.OpBoolSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<bool>? r1, List<bool>? r2) = await _prx.OpBoolListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<bool> { true };
                (r1, r2) = await _prx.OpBoolListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (bool?[]? r1, bool?[]? r2) = await _prx.OpOptionalBoolSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                bool?[] p1 = new bool?[] { true, null, false };
                (r1, r2) = await _prx.OpOptionalBoolSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (short[]? r1, short[]? r2) = await _prx.OpShortSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                short[] p1 = new short[] { 42 };
                (r1, r2) = await _prx.OpShortSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<short>? r1, List<short>? r2) = await _prx.OpShortListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<short> { 42 };
                (r1, r2) = await _prx.OpShortListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (short?[]? r1, short?[]? r2) = await _prx.OpOptionalShortSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                short?[] p1 = new short?[] { 42, null, 34 };
                (r1, r2) = await _prx.OpOptionalShortSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (int[]? r1, int[]? r2) = await _prx.OpIntSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                int[] p1 = new int[] { 42 };
                (r1, r2) = await _prx.OpIntSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<int>? r1, List<int>? r2) = await _prx.OpIntListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<int> { 42 };
                (r1, r2) = await _prx.OpIntListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (int?[]? r1, int?[]? r2) = await _prx.OpOptionalIntSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                int?[]? p1 = new int?[] { 42, null, 43 };
                (r1, r2) = await _prx.OpOptionalIntSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (long[]? r1, long[]? r2) = await _prx.OpLongSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                long[] p1 = new long[] { 42 };
                (r1, r2) = await _prx.OpLongSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<long>? r1, List<long>? r2) = await _prx.OpLongListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<long> { 42 };
                (r1, r2) = await _prx.OpLongListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (long?[]? r1, long?[]? r2) = await _prx.OpOptionalLongSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                long?[] p1 = new long?[] { 42, null, 43 };
                (r1, r2) = await _prx.OpOptionalLongSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (float[]? r1, float[]? r2) = await _prx.OpFloatSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                float[] p1 = new float[] { 42 };
                (r1, r2) = await _prx.OpFloatSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<float>? r1, List<float>? r2) = await _prx.OpFloatListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<float> { 42 };
                (r1, r2) = await _prx.OpFloatListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (float?[]? r1, float?[]? r2) = await _prx.OpOptionalFloatSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                float?[] p1 = new float?[] { 42, null, 43 };
                (r1, r2) = await _prx.OpOptionalFloatSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (double[]? r1, double[]? r2) = await _prx.OpDoubleSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                double[] p1 = new double[] { 42 };
                (r1, r2) = await _prx.OpDoubleSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<double>? r1, List<double>? r2) = await _prx.OpDoubleListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<double> { 42 };
                (r1, r2) = await _prx.OpDoubleListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (double?[]? r1, double?[]? r2) = await _prx.OpOptionalDoubleSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                double?[] p1 = new double?[] { 42 };
                (r1, r2) = await _prx.OpOptionalDoubleSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (string[]? r1, string[]? r2) = await _prx.OpStringSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                string[] p1 = new string[] { "hello" };
                (r1, r2) = await _prx.OpStringSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<string>? r1, List<string>? r2) = await _prx.OpStringListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<string> { "hello" };
                (r1, r2) = await _prx.OpStringListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (string?[]? r1, string?[]? r2) = await _prx.OpOptionalStringSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                string?[] p1 = new string?[] { "hello" };
                (r1, r2) = await _prx.OpOptionalStringSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                string[]? r1 = await _prx.OpStringSeqMarshaledResultAsync(null);
                Assert.That(r1, Is.Null);

                string[] p1 = new string[] { "hello" };
                r1 = await _prx.OpStringSeqMarshaledResultAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
            }

            {
                (MyStruct[]? r1, MyStruct[]? r2) = await _prx.OpMyStructSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyStruct[] { new MyStruct(1, 1) };
                (r1, r2) = await _prx.OpMyStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<MyStruct>? r1, List<MyStruct>? r2) = await _prx.OpMyStructListAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new List<MyStruct> { new MyStruct(1, 1) };
                (r1, r2) = await _prx.OpMyStructListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (MyStruct?[]? r1, MyStruct?[]? r2) = await _prx.OpOptionalMyStructSeqAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new MyStruct?[] { new MyStruct(1, 1), null, new MyStruct(1, 1) };
                (r1, r2) = await _prx.OpOptionalMyStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (AnotherStruct[]? r1, AnotherStruct[]? r2) = await _prx.OpAnotherStructSeqAsync(null);
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
                (r1, r2) = await _prx.OpAnotherStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<AnotherStruct>? r1, List<AnotherStruct>? r2) = await _prx.OpAnotherStructListAsync(null);
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
                (r1, r2) = await _prx.OpAnotherStructListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (AnotherStruct?[]? r1, AnotherStruct?[]? r2) = await _prx.OpOptionalAnotherStructSeqAsync(null);
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
                (r1, r2) = await _prx.OpOptionalAnotherStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (Dictionary<int, int>? r1, Dictionary<int, int>? r2) = await _prx.OpIntDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<int, int> { { 1, 1 } };
                (r1, r2) = await _prx.OpIntDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (SortedDictionary<int, int>? r1, SortedDictionary<int, int>? r2) = await _prx.OpIntSortedDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new SortedDictionary<int, int> { { 1, 1 } };
                (r1, r2) = await _prx.OpIntSortedDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (Dictionary<int, int?>? r1, Dictionary<int, int?>? r2) = await _prx.OpOptionalIntDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<int, int?> { { 1, 1 } };
                (r1, r2) = await _prx.OpOptionalIntDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (SortedDictionary<int, int?>? r1, SortedDictionary<int, int?>? r2) = await _prx.OpOptionalIntSortedDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new SortedDictionary<int, int?> { { 1, 1 } };
                (r1, r2) = await _prx.OpOptionalIntSortedDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                Dictionary<int, int>? r1 = await _prx.OpIntDictMarshaledResultAsync(null);
                Assert.That(r1, Is.Null);

                var p1 = new Dictionary<int, int> { { 1, 1 } };
                r1 = await _prx.OpIntDictMarshaledResultAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
            }

            {
                (Dictionary<string, string>? r1, Dictionary<string, string>? r2) = await _prx.OpStringDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<string, string> { { "a", "b" } };
                (r1, r2) = await _prx.OpStringDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (SortedDictionary<string, string>? r1, SortedDictionary<string, string>? r2) =
                    await _prx.OpStringSortedDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new SortedDictionary<string, string> { { "a", "b" } };
                (r1, r2) = await _prx.OpStringSortedDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (Dictionary<string, string?>? r1, Dictionary<string, string?>? r2) = await _prx.OpOptionalStringDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new Dictionary<string, string?> { { "a", "b" } };
                (r1, r2) = await _prx.OpOptionalStringDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (SortedDictionary<string, string?>? r1, SortedDictionary<string, string?>? r2) = await _prx.OpOptionalStringSortedDictAsync(null);
                Assert.That(r1, Is.Null);
                Assert.That(r2, Is.Null);

                var p1 = new SortedDictionary<string, string?> { { "a", "b" } };
                (r1, r2) = await _prx.OpOptionalStringSortedDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }
        }

        private static void CheckMultiTaggedHasNoValue(MultiTagged multiTagged)
        {
            Assert.That(multiTagged.MByte.HasValue, Is.False);
            Assert.That(multiTagged.MBool.HasValue, Is.False);
            Assert.That(multiTagged.MShort.HasValue, Is.False);
            Assert.That(multiTagged.MInt.HasValue, Is.False);
            Assert.That(multiTagged.MLong.HasValue, Is.False);
            Assert.That(multiTagged.MFloat.HasValue, Is.False);
            Assert.That(multiTagged.MDouble.HasValue, Is.False);
            Assert.That(multiTagged.MUShort.HasValue, Is.False);
            Assert.That(multiTagged.MUInt.HasValue, Is.False);
            Assert.That(multiTagged.MULong.HasValue, Is.False);
            Assert.That(multiTagged.MVarInt.HasValue, Is.False);
            Assert.That(multiTagged.MVarLong.HasValue, Is.False);
            Assert.That(multiTagged.MVarUInt.HasValue, Is.False);
            Assert.That(multiTagged.MVarULong.HasValue, Is.False);
            Assert.That(multiTagged.MString, Is.Null);
            Assert.That(multiTagged.MMyEnum.HasValue, Is.False);
            Assert.That(multiTagged.MMyStruct.HasValue, Is.False);
            Assert.That(multiTagged.MAnotherStruct.HasValue, Is.False);

            Assert.That(multiTagged.MByteSeq, Is.Null);
            Assert.That(multiTagged.MStringSeq, Is.Null);
            Assert.That(multiTagged.MShortSeq, Is.Null);
            Assert.That(multiTagged.MMyEnumSeq, Is.Null);
            Assert.That(multiTagged.MMyStructSeq, Is.Null);
            Assert.That(multiTagged.MAnotherStructSeq, Is.Null);

            Assert.That(multiTagged.MIntDict, Is.Null);
            Assert.That(multiTagged.MStringDict, Is.Null);
            Assert.That(multiTagged.MUShortSeq, Is.Null);
            Assert.That(multiTagged.MVarULongSeq, Is.Null);
            Assert.That(multiTagged.MVarIntSeq, Is.Null);

            Assert.That(multiTagged.MByteDict, Is.Null);
            Assert.That(multiTagged.MMyStructDict, Is.Null);
            Assert.That(multiTagged.MAnotherStructDict, Is.Null);
        }
    }

    public class TaggedOperations : Service, ITaggedOperations
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

        public ValueTask OpDerivedExceptionAsync(
            int? p1,
            string? p2,
            AnotherStruct? p3,
            Dispatch dispatch,
            CancellationToken cancel) => throw new DerivedException(false, p1, p2, p3, p2, p3);

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

        public ValueTask<ITaggedOperations.OpIntDictMarshaledResultMarshaledReturnValue> OpIntDictMarshaledResultAsync(
            Dictionary<int, int>? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(new ITaggedOperations.OpIntDictMarshaledResultMarshaledReturnValue(p1, dispatch));

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

        public ValueTask<ITaggedOperations.OpMyStructMarshaledResultMarshaledReturnValue> OpMyStructMarshaledResultAsync(
            MyStruct? p1,
            Dispatch dispatch,
            CancellationToken cancel) =>
            new(new ITaggedOperations.OpMyStructMarshaledResultMarshaledReturnValue(p1, dispatch));

        public ValueTask<(IEnumerable<MyStruct>? R1, IEnumerable<MyStruct>? R2)> OpMyStructSeqAsync(
            MyStruct[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyStruct?>? R1, IEnumerable<MyStruct?>? R2)> OpOptionalMyStructSeqAsync(
            MyStruct?[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask OpRequiredExceptionAsync(
            int? p1,
            string? p2,
            AnotherStruct? p3,
            Dispatch dispatch,
            CancellationToken cancel) =>
            throw new RequiredException(false, p1, p2, p3, p2 ?? "test", p3 ?? new AnotherStruct());

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

        public ValueTask<ITaggedOperations.OpStringSeqMarshaledResultMarshaledReturnValue> OpStringSeqMarshaledResultAsync(
            string[]? p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(new ITaggedOperations.OpStringSeqMarshaledResultMarshaledReturnValue(p1, dispatch));

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

        public ValueTask OpTaggedExceptionAsync(
            int? p1,
            string? p2,
            AnotherStruct? p3,
            Dispatch dispatch,
            CancellationToken cancel) => throw new TaggedException(false, p1, p2, p3);

        public ValueTask OpVoidAsync(
            Dispatch dispatch,
            CancellationToken cancel) => default;

        public ValueTask<AnyClass> PingPongAsync(
            AnyClass o,
            Dispatch dispatch,
            CancellationToken cancel) => new(o);
    }
}
