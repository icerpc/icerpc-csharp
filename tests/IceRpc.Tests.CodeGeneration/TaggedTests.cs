// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.CodeGeneration
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public class TaggedTests
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly ITaggedOperationsPrx _prx;

        public TaggedTests()
        {
            _communicator = new Communicator();
            _server = new Server
            {
                Communicator = _communicator,
                Dispatcher = new TaggedOperations(),
            };
            _server.Listen();
            _prx = _server.CreateRelativeProxy<ITaggedOperationsPrx>("/");
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
        }

        [Test]
        public void Tagged_DataMembers()
        {
            var oneTagged = new OneTagged();
            Assert.IsFalse(oneTagged.A.HasValue);

            oneTagged = new OneTagged(16);
            Assert.AreEqual(16, oneTagged.A);

            CheckMultiTaggedHasNoValue(new MultiTagged());
        }

        [Test]
        public async Task Tagged_Parameters()
        {
            var oneTagged = (OneTagged)await _prx.PingPongAsync(new OneTagged());
            Assert.IsFalse(oneTagged.A.HasValue);

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
                IOperationsPrx.Parse("ice+tcp://localhost/hello", _communicator),
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

            Assert.IsNull(multiTagged1.MByteSeq);
            CollectionAssert.AreEqual(multiTagged.MStringSeq, multiTagged1.MStringSeq);
            Assert.IsNull(multiTagged1.MShortSeq);
            CollectionAssert.AreEqual(multiTagged.MMyEnumSeq, multiTagged1.MMyEnumSeq);
            Assert.IsNull(multiTagged1.MMyStructSeq);
            CollectionAssert.AreEqual(multiTagged.MAnotherStructSeq, multiTagged1.MAnotherStructSeq);

            Assert.IsNull(multiTagged1.MIntDict);
            CollectionAssert.AreEqual(multiTagged.MStringDict, multiTagged1.MStringDict);
            Assert.IsNull(multiTagged1.MUShortSeq);
            Assert.IsNull(multiTagged1.MVarULongSeq);
            CollectionAssert.AreEqual(multiTagged.MVarIntSeq, multiTagged1.MVarIntSeq);

            CollectionAssert.AreEqual(multiTagged.MByteDict, multiTagged1.MByteDict);
            Assert.IsNull(multiTagged1.MMyStructDict);
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
            Assert.IsNull(multiTagged1.MStringSeq);
            CollectionAssert.AreEqual(multiTagged.MShortSeq, multiTagged1.MShortSeq);
            Assert.IsNull(multiTagged1.MMyEnumSeq);
            CollectionAssert.AreEqual(multiTagged.MMyStructSeq, multiTagged1.MMyStructSeq);
            Assert.IsNull(multiTagged1.MAnotherStructSeq);

            CollectionAssert.AreEqual(multiTagged.MIntDict, multiTagged1.MIntDict);
            Assert.IsNull(multiTagged1.MStringDict);
            CollectionAssert.AreEqual(multiTagged.MUShortSeq, multiTagged1.MUShortSeq);
            Assert.IsNull(multiTagged1.MVarULongSeq);
            CollectionAssert.AreEqual(multiTagged.MVarIntSeq, multiTagged1.MVarIntSeq);

            Assert.IsNull(multiTagged1.MByteDict);
            CollectionAssert.AreEqual(multiTagged.MMyStructDict, multiTagged1.MMyStructDict);
            Assert.IsNull(multiTagged1.MAnotherStructDict);

            using var requestFrame = OutgoingRequestFrame.WithArgs(
                    _prx,
                    "opVoid",
                    idempotent: false,
                    compress: false,
                    format: default,
                    context: null,
                    (15, "test"),
                    (OutputStream ostr, in (int n, string s) value) =>
                    {
                        ostr.WriteTaggedInt(1, value.n);
                        ostr.WriteTaggedString(1, value.s); // duplicate tag ignored by the server
                    });

            using IncomingResponseFrame response = await _prx.InvokeAsync(requestFrame);
            Assert.AreEqual(ResultType.Success, response.ResultType);

            var b = (B)await _prx.PingPongAsync(new B());
            Assert.IsFalse(b.MInt2.HasValue);
            Assert.IsFalse(b.MInt3.HasValue);
            Assert.IsFalse(b.MInt4.HasValue);
            Assert.IsFalse(b.MInt6.HasValue);

            b = (B)await _prx.PingPongAsync(new B(10, 11, 12, 13, 0, null));
            Assert.AreEqual(10, b.MInt1);
            Assert.AreEqual(11, b.MInt2);
            Assert.AreEqual(12, b.MInt3);
            Assert.AreEqual(13, b.MInt4);
            Assert.AreEqual(0, b.MInt5);
            Assert.IsFalse(b.MInt6.HasValue);

            {
                (byte? r1, byte? r2) = await _prx.OpByteAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                (r1, r2) = await _prx.OpByteAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (bool? r1, bool? r2) = await _prx.OpBoolAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                (r1, r2) = await _prx.OpBoolAsync(true);
                Assert.IsTrue(r1);
                Assert.IsTrue(r2);
            }

            {
                (short? r1, short? r2) = await _prx.OpShortAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                (r1, r2) = await _prx.OpShortAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (int? r1, int? r2) = await _prx.OpIntAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                (r1, r2) = await _prx.OpIntAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (long? r1, long? r2) = await _prx.OpLongAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                (r1, r2) = await _prx.OpLongAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (float? r1, float? r2) = await _prx.OpFloatAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                (r1, r2) = await _prx.OpFloatAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (double? r1, double? r2) = await _prx.OpDoubleAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                (r1, r2) = await _prx.OpDoubleAsync(42);
                Assert.AreEqual(42, r1);
                Assert.AreEqual(42, r2);
            }

            {
                (string? r1, string? r2) = await _prx.OpStringAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                (r1, r2) = await _prx.OpStringAsync("hello");
                Assert.AreEqual("hello", r1);
                Assert.AreEqual("hello", r2);
            }

            {
                (MyEnum? r1, MyEnum? r2) = await _prx.OpMyEnumAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                (r1, r2) = await _prx.OpMyEnumAsync(MyEnum.enum1);
                Assert.AreEqual(MyEnum.enum1, r1);
                Assert.AreEqual(MyEnum.enum1, r2);
            }

            {
                (MyStruct? r1, MyStruct? r2) = await _prx.OpMyStructAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new MyStruct(1, 1);
                (r1, r2) = await _prx.OpMyStructAsync(p1);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p1, r2);
            }

            {
                MyStruct? r1 = await _prx.OpMyStructMarshaledResultAsync(null);
                Assert.IsNull(r1);

                var p1 = new MyStruct(1, 1);
                r1 = await _prx.OpMyStructMarshaledResultAsync(p1);
                Assert.AreEqual(p1, r1);
            }

            {
                (AnotherStruct? r1, AnotherStruct? r2) = await _prx.OpAnotherStructAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new AnotherStruct(
                    "hello",
                    IOperationsPrx.Parse("ice+tcp://localhost/hello", _communicator),
                    MyEnum.enum1,
                    new MyStruct(1, 1));
                (r1, r2) = await _prx.OpAnotherStructAsync(p1);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p1, r2);
            }

            {
                (byte[]? r1, byte[]? r2) = await _prx.OpByteSeqAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new byte[] { 42 };
                (r1, r2) = await _prx.OpByteSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<byte>? r1, List<byte>? r2) = await _prx.OpByteListAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new List<byte> { 42 };
                (r1, r2) = await _prx.OpByteListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (bool[]? r1, bool[]? r2) = await _prx.OpBoolSeqAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new bool[] { true };
                (r1, r2) = await _prx.OpBoolSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<bool>? r1, List<bool>? r2) = await _prx.OpBoolListAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new List<bool> { true };
                (r1, r2) = await _prx.OpBoolListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (short[]? r1, short[]? r2) = await _prx.OpShortSeqAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new short[] { 42 };
                (r1, r2) = await _prx.OpShortSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<short>? r1, List<short>? r2) = await _prx.OpShortListAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new List<short> { 42 };
                (r1, r2) = await _prx.OpShortListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (int[]? r1, int[]? r2) = await _prx.OpIntSeqAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new int[] { 42 };
                (r1, r2) = await _prx.OpIntSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<int>? r1, List<int>? r2) = await _prx.OpIntListAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new List<int> { 42 };
                (r1, r2) = await _prx.OpIntListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (long[]? r1, long[]? r2) = await _prx.OpLongSeqAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new long[] { 42 };
                (r1, r2) = await _prx.OpLongSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<long>? r1, List<long>? r2) = await _prx.OpLongListAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new List<long> { 42 };
                (r1, r2) = await _prx.OpLongListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (float[]? r1, float[]? r2) = await _prx.OpFloatSeqAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new float[] { 42 };
                (r1, r2) = await _prx.OpFloatSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<float>? r1, List<float>? r2) = await _prx.OpFloatListAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new List<float> { 42 };
                (r1, r2) = await _prx.OpFloatListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (double[]? r1, double[]? r2) = await _prx.OpDoubleSeqAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new double[] { 42 };
                (r1, r2) = await _prx.OpDoubleSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<double>? r1, List<double>? r2) = await _prx.OpDoubleListAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new List<double> { 42 };
                (r1, r2) = await _prx.OpDoubleListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (string[]? r1, string[]? r2) = await _prx.OpStringSeqAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new string[] { "hello" };
                (r1, r2) = await _prx.OpStringSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<string>? r1, List<string>? r2) = await _prx.OpStringListAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new List<string> { "hello" };
                (r1, r2) = await _prx.OpStringListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                string[]? r1 = await _prx.OpStringSeqMarshaledResultAsync(null);
                Assert.IsNull(r1);

                var p1 = new string[] { "hello" };
                r1 = await _prx.OpStringSeqMarshaledResultAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
            }

            {
                (MyStruct[]? r1, MyStruct[]? r2) = await _prx.OpMyStructSeqAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new MyStruct[] { new MyStruct(1, 1) };
                (r1, r2) = await _prx.OpMyStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<MyStruct>? r1, List<MyStruct>? r2) = await _prx.OpMyStructListAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new List<MyStruct> { new MyStruct(1, 1) };
                (r1, r2) = await _prx.OpMyStructListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (AnotherStruct[]? r1, AnotherStruct[]? r2) = await _prx.OpAnotherStructSeqAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new AnotherStruct[]
                {
                    new AnotherStruct(
                        "hello",
                        IOperationsPrx.Parse("ice+tcp://localhost/hello", _communicator),
                        MyEnum.enum1,
                        new MyStruct(1, 1))
                };
                (r1, r2) = await _prx.OpAnotherStructSeqAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (List<AnotherStruct>? r1, List<AnotherStruct>? r2) = await _prx.OpAnotherStructListAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new List<AnotherStruct>
                {
                    new AnotherStruct(
                        "hello",
                        IOperationsPrx.Parse("ice+tcp://localhost/hello", _communicator),
                        MyEnum.enum1,
                        new MyStruct(1, 1))
                };
                (r1, r2) = await _prx.OpAnotherStructListAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                (Dictionary<int, int>? r1, Dictionary<int, int>? r2) = await _prx.OpIntDictAsync(null);
                Assert.IsNull(r1);
                Assert.IsNull(r2);

                var p1 = new Dictionary<int, int> { { 1, 1 } };
                (r1, r2) = await _prx.OpIntDictAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
                CollectionAssert.AreEqual(p1, r2);
            }

            {
                Dictionary<int, int>? r1 = await _prx.OpIntDictMarshaledResultAsync(null);
                Assert.IsNull(r1);

                var p1 = new Dictionary<int, int> { { 1, 1 } };
                r1 = await _prx.OpIntDictMarshaledResultAsync(p1);
                CollectionAssert.AreEqual(p1, r1);
            }
        }

        private static void CheckMultiTaggedHasNoValue(MultiTagged multiTagged)
        {
            Assert.IsFalse(multiTagged.MByte.HasValue);
            Assert.IsFalse(multiTagged.MBool.HasValue);
            Assert.IsFalse(multiTagged.MShort.HasValue);
            Assert.IsFalse(multiTagged.MInt.HasValue);
            Assert.IsFalse(multiTagged.MLong.HasValue);
            Assert.IsFalse(multiTagged.MFloat.HasValue);
            Assert.IsFalse(multiTagged.MDouble.HasValue);
            Assert.IsFalse(multiTagged.MUShort.HasValue);
            Assert.IsFalse(multiTagged.MUInt.HasValue);
            Assert.IsFalse(multiTagged.MULong.HasValue);
            Assert.IsFalse(multiTagged.MVarInt.HasValue);
            Assert.IsFalse(multiTagged.MVarLong.HasValue);
            Assert.IsFalse(multiTagged.MVarUInt.HasValue);
            Assert.IsFalse(multiTagged.MVarULong.HasValue);
            Assert.IsNull(multiTagged.MString);
            Assert.IsFalse(multiTagged.MMyEnum.HasValue);
            Assert.IsFalse(multiTagged.MMyStruct.HasValue);
            Assert.IsFalse(multiTagged.MAnotherStruct.HasValue);

            Assert.IsNull(multiTagged.MByteSeq);
            Assert.IsNull(multiTagged.MStringSeq);
            Assert.IsNull(multiTagged.MShortSeq);
            Assert.IsNull(multiTagged.MMyEnumSeq);
            Assert.IsNull(multiTagged.MMyStructSeq);
            Assert.IsNull(multiTagged.MAnotherStructSeq);

            Assert.IsNull(multiTagged.MIntDict);
            Assert.IsNull(multiTagged.MStringDict);
            Assert.IsNull(multiTagged.MUShortSeq);
            Assert.IsNull(multiTagged.MVarULongSeq);
            Assert.IsNull(multiTagged.MVarIntSeq);

            Assert.IsNull(multiTagged.MByteDict);
            Assert.IsNull(multiTagged.MMyStructDict);
            Assert.IsNull(multiTagged.MAnotherStructDict);
        }
    }

    public class TaggedOperations : IAsyncTaggedOperations
    {
        public ValueTask<(AnotherStruct? R1, AnotherStruct? R2)> OpAnotherStructAsync(
            AnotherStruct? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<AnotherStruct>? R1, IEnumerable<AnotherStruct>? R2)> OpAnotherStructListAsync(
            List<AnotherStruct>? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<AnotherStruct>? R1, IEnumerable<AnotherStruct>? R2)> OpAnotherStructSeqAsync(
            AnotherStruct[]? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(bool? R1, bool? R2)> OpBoolAsync(
            bool? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<bool>? R1, IEnumerable<bool>? R2)> OpBoolListAsync(
            List<bool>? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<bool> R1, ReadOnlyMemory<bool> R2)> OpBoolSeqAsync(
            bool[]? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(byte? R1, byte? R2)> OpByteAsync(
            byte? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<byte>? R1, IEnumerable<byte>? R2)> OpByteListAsync(
            List<byte>? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<byte> R1, ReadOnlyMemory<byte> R2)> OpByteSeqAsync(
            byte[]? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask OpDerivedExceptionAsync(
            int? p1,
            string? p2,
            AnotherStruct? p3,
            Current current,
            CancellationToken cancel) => throw new DerivedException(false, p1, p2, p3, p2, p3);

        public ValueTask<(double? R1, double? R2)> OpDoubleAsync(
            double? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<double>? R1, IEnumerable<double>? R2)> OpDoubleListAsync(
            List<double>? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<double> R1, ReadOnlyMemory<double> R2)> OpDoubleSeqAsync(
            double[]? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(float? R1, float? R2)> OpFloatAsync(
            float? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<float>? R1, IEnumerable<float>? R2)> OpFloatListAsync(
            List<float>? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<float> R1, ReadOnlyMemory<float> R2)> OpFloatSeqAsync(
            float[]? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(int? R1, int? R2)> OpIntAsync(
            int? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IReadOnlyDictionary<int, int>? R1, IReadOnlyDictionary<int, int>? R2)> OpIntDictAsync(
            Dictionary<int, int>? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<ITaggedOperations.OpIntDictMarshaledResultMarshaledReturnValue> OpIntDictMarshaledResultAsync(
            Dictionary<int, int>? p1,
            Current current,
            CancellationToken cancel) => new(new ITaggedOperations.OpIntDictMarshaledResultMarshaledReturnValue(p1, current));

        public ValueTask<(IEnumerable<int>? R1, IEnumerable<int>? R2)> OpIntListAsync(
            List<int>? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<int> R1, ReadOnlyMemory<int> R2)> OpIntSeqAsync(
            int[]? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(long? R1, long? R2)> OpLongAsync(
            long? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<long>? R1, IEnumerable<long>? R2)> OpLongListAsync(
            List<long>? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<long> R1, ReadOnlyMemory<long> R2)> OpLongSeqAsync(
            long[]? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(MyEnum? R1, MyEnum? R2)> OpMyEnumAsync(
            MyEnum? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(MyStruct? R1, MyStruct? R2)> OpMyStructAsync(
            MyStruct? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<MyStruct>? R1, IEnumerable<MyStruct>? R2)> OpMyStructListAsync(
            List<MyStruct>? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<ITaggedOperations.OpMyStructMarshaledResultMarshaledReturnValue> OpMyStructMarshaledResultAsync(
            MyStruct? p1,
            Current current,
            CancellationToken cancel) =>
            new(new ITaggedOperations.OpMyStructMarshaledResultMarshaledReturnValue(p1, current));

        public ValueTask<(IEnumerable<MyStruct>? R1, IEnumerable<MyStruct>? R2)> OpMyStructSeqAsync(
            MyStruct[]? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask OpRequiredExceptionAsync(
            int? p1,
            string? p2,
            AnotherStruct? p3,
            Current current,
            CancellationToken cancel) =>
            throw new RequiredException(false, p1, p2, p3, p2 ?? "test", p3 ?? new AnotherStruct());

        public ValueTask<(short? R1, short? R2)> OpShortAsync(
            short? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<short>? R1, IEnumerable<short>? R2)> OpShortListAsync(
            List<short>? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(ReadOnlyMemory<short> R1, ReadOnlyMemory<short> R2)> OpShortSeqAsync(
            short[]? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(string? R1, string? R2)> OpStringAsync(
            string? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IReadOnlyDictionary<string, string>? R1, IReadOnlyDictionary<string, string>? R2)> OpStringDictAsync(
            Dictionary<string, string>? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<string>? R1, IEnumerable<string>? R2)> OpStringListAsync(
            List<string>? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<(IEnumerable<string>? R1, IEnumerable<string>? R2)> OpStringSeqAsync(
            string[]? p1,
            Current current,
            CancellationToken cancel) => new((p1, p1));

        public ValueTask<ITaggedOperations.OpStringSeqMarshaledResultMarshaledReturnValue> OpStringSeqMarshaledResultAsync(
            string[]? p1,
            Current current,
            CancellationToken cancel) => new(new ITaggedOperations.OpStringSeqMarshaledResultMarshaledReturnValue(p1, current));

        public ValueTask OpTaggedExceptionAsync(
            int? p1,
            string? p2,
            AnotherStruct? p3,
            Current current,
            CancellationToken cancel) => throw new TaggedException(false, p1, p2, p3);

        public ValueTask OpVoidAsync(
            Current current,
            CancellationToken cancel) => default;

        public ValueTask<AnyClass> PingPongAsync(
            AnyClass o,
            Current current,
            CancellationToken cancel) => new(o);
    }
}
