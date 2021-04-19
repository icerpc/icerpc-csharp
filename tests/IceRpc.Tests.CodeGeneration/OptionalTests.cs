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
    public class OptionalTests
    {
        private readonly Communicator _communicator;
        private readonly Server _server;
        private readonly IOptionalOperationsPrx _prx;

        public OptionalTests()
        {
            _communicator = new Communicator();
            _server = new Server()
            {
                Communicator = _communicator,
                Dispatcher = new OptionalOperations()
            };
            _server.Listen();
            _prx = _server.CreateRelativeProxy<IOptionalOperationsPrx>("/");
        }

        [OneTimeTearDown]
        public async Task TearDownAsync()
        {
            await _server.DisposeAsync();
            await _communicator.DisposeAsync();
        }

        [Test]
        public void Optional_DataMembers()
        {
            var oneOptional = new OneOptional();
            Assert.IsFalse(oneOptional.A.HasValue);

            oneOptional = new OneOptional(16);
            Assert.AreEqual(16, oneOptional.A);

            CheckMultiOptionalHasNoValue(new MultiOptional());
        }

        [Test]
        public async Task Tagged_Parameters()
        {
            var oneOptional = (OneOptional?)await _prx.PingPongAsync(new OneOptional());
            Assert.IsNotNull(oneOptional);
            Assert.IsFalse(oneOptional.A.HasValue);

            oneOptional = (OneOptional?)await _prx.PingPongAsync(new OneOptional(16));
            Assert.IsNotNull(oneOptional);
            Assert.AreEqual(16, oneOptional.A);

            var multiOtional = (MultiOptional?)await _prx.PingPongAsync(new MultiOptional());
            Assert.IsNotNull(multiOtional);
            CheckMultiOptionalHasNoValue(multiOtional);

            multiOtional.MByte = 1;
            multiOtional.MShort = 1;
            multiOtional.MLong = 1;
            multiOtional.MDouble = 1.0;
            multiOtional.MUShort = 1;
            multiOtional.MULong = 1;
            multiOtional.MVarLong = 1;
            multiOtional.MString = "1";
            multiOtional.MMyEnum = MyEnum.enum1;
            multiOtional.MAnotherStruct = new AnotherStruct(
                "hello",
                IOperationsPrx.Parse("ice+tcp://localhost/hello", _communicator),
                MyEnum.enum1,
                new MyStruct(1, 1));

            multiOtional.MStringSeq = new string[] { "hello" };
            multiOtional.MMyEnumSeq = new MyEnum[] { MyEnum.enum1 };
            multiOtional.MAnotherStructSeq = new AnotherStruct[] { multiOtional.MAnotherStruct.Value };

            multiOtional.MStringDict = new Dictionary<string, string>()
            {
                { "key", "value" }
            };
            multiOtional.MVarIntSeq = new int[] { 1 };

            multiOtional.MByteDict = new Dictionary<byte, byte>() { { 1, 1 } };
            multiOtional.MAnotherStructDict = new Dictionary<string, AnotherStruct>()
            {
                { "key", multiOtional.MAnotherStruct.Value}
            };

            var multiOptional1 = (MultiOptional?)await _prx.PingPongAsync(multiOtional);
            Assert.IsNotNull(multiOptional1);
            Assert.AreEqual(multiOtional.MByte, multiOptional1.MByte);
            Assert.AreEqual(multiOtional.MBool, multiOptional1.MBool);
            Assert.AreEqual(multiOtional.MShort, multiOptional1.MShort);
            Assert.AreEqual(multiOtional.MInt, multiOptional1.MInt);
            Assert.AreEqual(multiOtional.MLong, multiOptional1.MLong);
            Assert.AreEqual(multiOtional.MFloat, multiOptional1.MFloat);
            Assert.AreEqual(multiOtional.MDouble, multiOptional1.MDouble);
            Assert.AreEqual(multiOtional.MUShort, multiOptional1.MUShort);
            Assert.AreEqual(multiOtional.MUInt, multiOptional1.MUInt);
            Assert.AreEqual(multiOtional.MULong, multiOptional1.MULong);
            Assert.AreEqual(multiOtional.MVarInt, multiOptional1.MVarInt);
            Assert.AreEqual(multiOtional.MVarLong, multiOptional1.MVarLong);
            Assert.AreEqual(multiOtional.MVarUInt, multiOptional1.MVarUInt);
            Assert.AreEqual(multiOtional.MVarULong, multiOptional1.MVarULong);
            Assert.AreEqual(multiOtional.MString, multiOptional1.MString);
            Assert.AreEqual(multiOtional.MMyEnum, multiOptional1.MMyEnum);
            Assert.AreEqual(multiOtional.MMyStruct, multiOptional1.MMyStruct);
            Assert.AreEqual(multiOtional.MAnotherStruct, multiOptional1.MAnotherStruct);

            Assert.IsNull(multiOptional1.MByteSeq);
            CollectionAssert.AreEqual(multiOtional.MStringSeq, multiOptional1.MStringSeq);
            Assert.IsNull(multiOptional1.MShortSeq);
            CollectionAssert.AreEqual(multiOtional.MMyEnumSeq, multiOptional1.MMyEnumSeq);
            Assert.IsNull(multiOptional1.MMyStructSeq);
            CollectionAssert.AreEqual(multiOtional.MAnotherStructSeq, multiOptional1.MAnotherStructSeq);

            Assert.IsNull(multiOptional1.MIntDict);
            CollectionAssert.AreEqual(multiOtional.MStringDict, multiOptional1.MStringDict);
            Assert.IsNull(multiOptional1.MUShortSeq);
            Assert.IsNull(multiOptional1.MVarULongSeq);
            CollectionAssert.AreEqual(multiOtional.MVarIntSeq, multiOptional1.MVarIntSeq);

            CollectionAssert.AreEqual(multiOtional.MByteDict, multiOptional1.MByteDict);
            Assert.IsNull(multiOptional1.MMyStructDict);
            CollectionAssert.AreEqual(multiOtional.MAnotherStructDict, multiOptional1.MAnotherStructDict);

            multiOtional = new MultiOptional();
            multiOtional.MBool = true;
            multiOtional.MInt = 1;
            multiOtional.MFloat = 1;
            multiOtional.MUShort = 1;
            multiOtional.MULong = 1;
            multiOtional.MVarLong = 1;
            multiOtional.MVarULong = 1;
            multiOtional.MMyEnum = MyEnum.enum1;
            multiOtional.MMyStruct = new MyStruct(1, 1);

            multiOtional.MByteSeq = new byte[] { 1 };
            multiOtional.MShortSeq = new short[] { 1 };
            multiOtional.MMyStructSeq = new MyStruct[] { new MyStruct(1, 1) };

            multiOtional.MIntDict = new Dictionary<int, int> { { 1, 1 } };
            multiOtional.MUShortSeq = new ushort[] { 1 };
            multiOtional.MVarIntSeq = new int[] { 1 };
            multiOtional.MMyStructDict = new Dictionary<MyStruct, MyStruct>()
            {
                { new MyStruct(1, 1), new MyStruct(1, 1) }
            };

            multiOptional1 = (MultiOptional?)await _prx.PingPongAsync(multiOtional);
            Assert.IsNotNull(multiOptional1);
            Assert.AreEqual(multiOtional.MByte, multiOptional1.MByte);
            Assert.AreEqual(multiOtional.MBool, multiOptional1.MBool);
            Assert.AreEqual(multiOtional.MShort, multiOptional1.MShort);
            Assert.AreEqual(multiOtional.MInt, multiOptional1.MInt);
            Assert.AreEqual(multiOtional.MLong, multiOptional1.MLong);
            Assert.AreEqual(multiOtional.MFloat, multiOptional1.MFloat);
            Assert.AreEqual(multiOtional.MDouble, multiOptional1.MDouble);
            Assert.AreEqual(multiOtional.MUShort, multiOptional1.MUShort);
            Assert.AreEqual(multiOtional.MUInt, multiOptional1.MUInt);
            Assert.AreEqual(multiOtional.MULong, multiOptional1.MULong);
            Assert.AreEqual(multiOtional.MVarInt, multiOptional1.MVarInt);
            Assert.AreEqual(multiOtional.MVarLong, multiOptional1.MVarLong);
            Assert.AreEqual(multiOtional.MVarUInt, multiOptional1.MVarUInt);
            Assert.AreEqual(multiOtional.MVarULong, multiOptional1.MVarULong);
            Assert.AreEqual(multiOtional.MString, multiOptional1.MString);
            Assert.AreEqual(multiOtional.MMyEnum, multiOptional1.MMyEnum);
            Assert.AreEqual(multiOtional.MMyStruct, multiOptional1.MMyStruct);
            Assert.AreEqual(multiOtional.MAnotherStruct, multiOptional1.MAnotherStruct);

            CollectionAssert.AreEqual(multiOtional.MByteSeq, multiOptional1.MByteSeq);
            Assert.IsNull(multiOptional1.MStringSeq);
            CollectionAssert.AreEqual(multiOtional.MShortSeq, multiOptional1.MShortSeq);
            Assert.IsNull(multiOptional1.MMyEnumSeq);
            CollectionAssert.AreEqual(multiOtional.MMyStructSeq, multiOptional1.MMyStructSeq);
            Assert.IsNull(multiOptional1.MAnotherStructSeq);

            CollectionAssert.AreEqual(multiOtional.MIntDict, multiOptional1.MIntDict);
            Assert.IsNull(multiOptional1.MStringDict);
            CollectionAssert.AreEqual(multiOtional.MUShortSeq, multiOptional1.MUShortSeq);
            Assert.IsNull(multiOptional1.MVarULongSeq);
            CollectionAssert.AreEqual(multiOtional.MVarIntSeq, multiOptional1.MVarIntSeq);

            Assert.IsNull(multiOptional1.MByteDict);
            CollectionAssert.AreEqual(multiOtional.MMyStructDict, multiOptional1.MMyStructDict);
            Assert.IsNull(multiOptional1.MAnotherStructDict);

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

        private static void CheckMultiOptionalHasNoValue(MultiOptional multiOtional)
        {
            Assert.IsFalse(multiOtional.MByte.HasValue);
            Assert.IsFalse(multiOtional.MBool.HasValue);
            Assert.IsFalse(multiOtional.MShort.HasValue);
            Assert.IsFalse(multiOtional.MInt.HasValue);
            Assert.IsFalse(multiOtional.MLong.HasValue);
            Assert.IsFalse(multiOtional.MFloat.HasValue);
            Assert.IsFalse(multiOtional.MDouble.HasValue);
            Assert.IsFalse(multiOtional.MUShort.HasValue);
            Assert.IsFalse(multiOtional.MUInt.HasValue);
            Assert.IsFalse(multiOtional.MULong.HasValue);
            Assert.IsFalse(multiOtional.MVarInt.HasValue);
            Assert.IsFalse(multiOtional.MVarLong.HasValue);
            Assert.IsFalse(multiOtional.MVarUInt.HasValue);
            Assert.IsFalse(multiOtional.MVarULong.HasValue);
            Assert.IsNull(multiOtional.MString);
            Assert.IsFalse(multiOtional.MMyEnum.HasValue);
            Assert.IsFalse(multiOtional.MMyStruct.HasValue);
            Assert.IsFalse(multiOtional.MAnotherStruct.HasValue);

            Assert.IsNull(multiOtional.MByteSeq);
            Assert.IsNull(multiOtional.MStringSeq);
            Assert.IsNull(multiOtional.MShortSeq);
            Assert.IsNull(multiOtional.MMyEnumSeq);
            Assert.IsNull(multiOtional.MMyStructSeq);
            Assert.IsNull(multiOtional.MAnotherStructSeq);

            Assert.IsNull(multiOtional.MIntDict);
            Assert.IsNull(multiOtional.MStringDict);
            Assert.IsNull(multiOtional.MUShortSeq);
            Assert.IsNull(multiOtional.MVarULongSeq);
            Assert.IsNull(multiOtional.MVarIntSeq);

            Assert.IsNull(multiOtional.MByteDict);
            Assert.IsNull(multiOtional.MMyStructDict);
            Assert.IsNull(multiOtional.MAnotherStructDict);
        }

        class OptionalOperations : IAsyncOptionalOperations
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
                bool[]? p1, Current current, CancellationToken cancel) => new((p1, p1));

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

            public ValueTask<IOptionalOperations.OpIntDictMarshaledResultMarshaledReturnValue> OpIntDictMarshaledResultAsync(
                Dictionary<int, int>? p1,
                Current current,
                CancellationToken cancel) =>
                new(new IOptionalOperations.OpIntDictMarshaledResultMarshaledReturnValue(p1, current));

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

            public ValueTask<IOptionalOperations.OpMyStructMarshaledResultMarshaledReturnValue> OpMyStructMarshaledResultAsync(
                MyStruct? p1,
                Current current,
                CancellationToken cancel) =>
                new(new IOptionalOperations.OpMyStructMarshaledResultMarshaledReturnValue(p1, current));

            public ValueTask<(IEnumerable<MyStruct>? R1, IEnumerable<MyStruct>? R2)> OpMyStructSeqAsync(
                MyStruct[]? p1,
                Current current,
                CancellationToken cancel) => new((p1, p1));

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

            public ValueTask<IOptionalOperations.OpStringSeqMarshaledResultMarshaledReturnValue> OpStringSeqMarshaledResultAsync(
                string[]? p1,
                Current current,
                CancellationToken cancel) => new(new IOptionalOperations.OpStringSeqMarshaledResultMarshaledReturnValue(p1, current));
            public ValueTask<AnyClass?> PingPongAsync(AnyClass? o, Current current, CancellationToken cancel) =>
                new(o);
        }
    }
}
