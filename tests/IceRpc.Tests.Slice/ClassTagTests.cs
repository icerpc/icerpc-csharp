// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public sealed class ClassTagTests : IAsyncDisposable
    {
        private readonly Connection _connection;
        private readonly Server _server;
        private readonly ClassTagPrx _prx;

        public ClassTagTests()
        {
            _server = new Server
            {
                Dispatcher = new ClassTag(),
                Endpoint = TestHelper.GetUniqueColocEndpoint()
            };
            _server.Listen();
            _connection = new Connection
            {
                RemoteEndpoint = _server.Endpoint
            };
            _prx = ClassTagPrx.FromConnection(_connection);
        }

        [OneTimeTearDown]
        public async ValueTask DisposeAsync()
        {
            await _server.DisposeAsync();
            await _connection.DisposeAsync();
        }

        [Test]
        public void ClassTag_DataMembers()
        {
            var oneTagged = new OneTagged();
            Assert.That(oneTagged.A.HasValue, Is.False);

            oneTagged = new OneTagged(16);
            Assert.AreEqual(16, oneTagged.A);

            CheckMultiTaggedHasNoValue(new MultiTagged());
        }

        [Test]
        public async Task ClassTag_Parameters()
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

    public class ClassTag : Service, IClassTag
    {
        public ValueTask<AnyClass> PingPongAsync(
            AnyClass o,
            Dispatch dispatch,
            CancellationToken cancel) => new(o);
    }
}
