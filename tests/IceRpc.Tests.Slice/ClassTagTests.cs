// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    public sealed class ClassTagTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly ClassTagPrx _prx;

        public ClassTagTests()
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .AddTransient<IDispatcher, ClassTag>()
                .BuildServiceProvider();
            _prx = _serviceProvider.GetProxy<ClassTagPrx>();
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public void ClassTag_DataMembers()
        {
            var oneTagged = new OneTagged();
            Assert.That(oneTagged.A.HasValue, Is.False);

            oneTagged = new OneTagged(16);
           Assert.That(oneTagged.A, Is.EqualTo(16));

            CheckMultiTaggedHasNoValue(new MultiTagged());
        }

        [Test]
        public async Task ClassTag_Parameters()
        {
            var oneTagged = (OneTagged)await _prx.PingPongAsync(new OneTagged());
            Assert.That(oneTagged.A.HasValue, Is.False);

            oneTagged = (OneTagged)await _prx.PingPongAsync(new OneTagged(16));
           Assert.That(oneTagged.A, Is.EqualTo(16));

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
            multiTagged.MAnotherCompactStruct = new AnotherCompactStruct(
                "hello",
                OperationsPrx.Parse("icerpc://localhost/hello"),
                MyEnum.enum1,
                new MyCompactStruct(1, 1));

            multiTagged.MStringSeq = new string[] { "hello" };
            multiTagged.MMyEnumSeq = new MyEnum[] { MyEnum.enum1 };
            multiTagged.MAnotherCompactStructSeq = new AnotherCompactStruct[] { multiTagged.MAnotherCompactStruct.Value };

            multiTagged.MStringDict = new Dictionary<string, string>()
            {
                { "key", "value" }
            };
            multiTagged.MVarIntSeq = new int[] { 1 };

            multiTagged.MByteDict = new Dictionary<byte, byte>() { { 1, 1 } };
            multiTagged.MAnotherCompactStructDict = new Dictionary<string, AnotherCompactStruct>()
            {
                { "key", multiTagged.MAnotherCompactStruct.Value}
            };

            var multiTagged1 = (MultiTagged)await _prx.PingPongAsync(multiTagged);
           Assert.That(multiTagged1.MByte, Is.EqualTo(multiTagged.MByte));
           Assert.That(multiTagged1.MBool, Is.EqualTo(multiTagged.MBool));
           Assert.That(multiTagged1.MShort, Is.EqualTo(multiTagged.MShort));
           Assert.That(multiTagged1.MInt, Is.EqualTo(multiTagged.MInt));
           Assert.That(multiTagged1.MLong, Is.EqualTo(multiTagged.MLong));
           Assert.That(multiTagged1.MFloat, Is.EqualTo(multiTagged.MFloat));
           Assert.That(multiTagged1.MDouble, Is.EqualTo(multiTagged.MDouble));
           Assert.That(multiTagged1.MUShort, Is.EqualTo(multiTagged.MUShort));
           Assert.That(multiTagged1.MUInt, Is.EqualTo(multiTagged.MUInt));
           Assert.That(multiTagged1.MULong, Is.EqualTo(multiTagged.MULong));
           Assert.That(multiTagged1.MVarInt, Is.EqualTo(multiTagged.MVarInt));
           Assert.That(multiTagged1.MVarLong, Is.EqualTo(multiTagged.MVarLong));
           Assert.That(multiTagged1.MVarUInt, Is.EqualTo(multiTagged.MVarUInt));
           Assert.That(multiTagged1.MVarULong, Is.EqualTo(multiTagged.MVarULong));
           Assert.That(multiTagged1.MString, Is.EqualTo(multiTagged.MString));
           Assert.That(multiTagged1.MMyEnum, Is.EqualTo(multiTagged.MMyEnum));
           Assert.That(multiTagged1.MMyCompactStruct, Is.EqualTo(multiTagged.MMyCompactStruct));
           Assert.That(multiTagged1.MAnotherCompactStruct, Is.EqualTo(multiTagged.MAnotherCompactStruct));

            Assert.That(multiTagged1.MByteSeq, Is.Null);
           Assert.That(multiTagged1.MStringSeq, Is.EqualTo(multiTagged.MStringSeq));
            Assert.That(multiTagged1.MShortSeq, Is.Null);
           Assert.That(multiTagged1.MMyEnumSeq, Is.EqualTo(multiTagged.MMyEnumSeq));
            Assert.That(multiTagged1.MMyCompactStructSeq, Is.Null);
           Assert.That(multiTagged1.MAnotherCompactStructSeq, Is.EqualTo(multiTagged.MAnotherCompactStructSeq));

            Assert.That(multiTagged1.MIntDict, Is.Null);
           Assert.That(multiTagged1.MStringDict, Is.EqualTo(multiTagged.MStringDict));
            Assert.That(multiTagged1.MUShortSeq, Is.Null);
            Assert.That(multiTagged1.MVarULongSeq, Is.Null);
           Assert.That(multiTagged1.MVarIntSeq, Is.EqualTo(multiTagged.MVarIntSeq));

           Assert.That(multiTagged1.MByteDict, Is.EqualTo(multiTagged.MByteDict));
            Assert.That(multiTagged1.MMyCompactStructDict, Is.Null);
           Assert.That(multiTagged1.MAnotherCompactStructDict, Is.EqualTo(multiTagged.MAnotherCompactStructDict));

            multiTagged = new MultiTagged();
            multiTagged.MBool = true;
            multiTagged.MInt = 1;
            multiTagged.MFloat = 1;
            multiTagged.MUShort = 1;
            multiTagged.MULong = 1;
            multiTagged.MVarLong = 1;
            multiTagged.MVarULong = 1;
            multiTagged.MMyEnum = MyEnum.enum1;
            multiTagged.MMyCompactStruct = new MyCompactStruct(1, 1);

            multiTagged.MByteSeq = new byte[] { 1 };
            multiTagged.MShortSeq = new short[] { 1 };
            multiTagged.MMyCompactStructSeq = new MyCompactStruct[] { new MyCompactStruct(1, 1) };

            multiTagged.MIntDict = new Dictionary<int, int> { { 1, 1 } };
            multiTagged.MUShortSeq = new ushort[] { 1 };
            multiTagged.MVarIntSeq = new int[] { 1 };
            multiTagged.MMyCompactStructDict = new Dictionary<MyCompactStruct, MyCompactStruct>()
            {
                { new MyCompactStruct(1, 1), new MyCompactStruct(1, 1) }
            };

            multiTagged1 = (MultiTagged)await _prx.PingPongAsync(multiTagged);
           Assert.That(multiTagged1.MByte, Is.EqualTo(multiTagged.MByte));
           Assert.That(multiTagged1.MBool, Is.EqualTo(multiTagged.MBool));
           Assert.That(multiTagged1.MShort, Is.EqualTo(multiTagged.MShort));
           Assert.That(multiTagged1.MInt, Is.EqualTo(multiTagged.MInt));
           Assert.That(multiTagged1.MLong, Is.EqualTo(multiTagged.MLong));
           Assert.That(multiTagged1.MFloat, Is.EqualTo(multiTagged.MFloat));
           Assert.That(multiTagged1.MDouble, Is.EqualTo(multiTagged.MDouble));
           Assert.That(multiTagged1.MUShort, Is.EqualTo(multiTagged.MUShort));
           Assert.That(multiTagged1.MUInt, Is.EqualTo(multiTagged.MUInt));
           Assert.That(multiTagged1.MULong, Is.EqualTo(multiTagged.MULong));
           Assert.That(multiTagged1.MVarInt, Is.EqualTo(multiTagged.MVarInt));
           Assert.That(multiTagged1.MVarLong, Is.EqualTo(multiTagged.MVarLong));
           Assert.That(multiTagged1.MVarUInt, Is.EqualTo(multiTagged.MVarUInt));
           Assert.That(multiTagged1.MVarULong, Is.EqualTo(multiTagged.MVarULong));
           Assert.That(multiTagged1.MString, Is.EqualTo(multiTagged.MString));
           Assert.That(multiTagged1.MMyEnum, Is.EqualTo(multiTagged.MMyEnum));
           Assert.That(multiTagged1.MMyCompactStruct, Is.EqualTo(multiTagged.MMyCompactStruct));
           Assert.That(multiTagged1.MAnotherCompactStruct, Is.EqualTo(multiTagged.MAnotherCompactStruct));

           Assert.That(multiTagged1.MByteSeq, Is.EqualTo(multiTagged.MByteSeq));
            Assert.That(multiTagged1.MStringSeq, Is.Null);
           Assert.That(multiTagged1.MShortSeq, Is.EqualTo(multiTagged.MShortSeq));
            Assert.That(multiTagged1.MMyEnumSeq, Is.Null);
           Assert.That(multiTagged1.MMyCompactStructSeq, Is.EqualTo(multiTagged.MMyCompactStructSeq));
            Assert.That(multiTagged1.MAnotherCompactStructSeq, Is.Null);

           Assert.That(multiTagged1.MIntDict, Is.EqualTo(multiTagged.MIntDict));
            Assert.That(multiTagged1.MStringDict, Is.Null);
           Assert.That(multiTagged1.MUShortSeq, Is.EqualTo(multiTagged.MUShortSeq));
            Assert.That(multiTagged1.MVarULongSeq, Is.Null);
           Assert.That(multiTagged1.MVarIntSeq, Is.EqualTo(multiTagged.MVarIntSeq));

            Assert.That(multiTagged1.MByteDict, Is.Null);
           Assert.That(multiTagged1.MMyCompactStructDict, Is.EqualTo(multiTagged.MMyCompactStructDict));
            Assert.That(multiTagged1.MAnotherCompactStructDict, Is.Null);

            var b = (B)await _prx.PingPongAsync(new B());
            Assert.That(b.MInt2.HasValue, Is.False);
            Assert.That(b.MInt3.HasValue, Is.False);
            Assert.That(b.MInt4.HasValue, Is.False);
            Assert.That(b.MInt6.HasValue, Is.False);

            b = (B)await _prx.PingPongAsync(new B(10, 11, 12, 13, 0, null));
           Assert.That(b.MInt1, Is.EqualTo(10));
           Assert.That(b.MInt2, Is.EqualTo(11));
           Assert.That(b.MInt3, Is.EqualTo(12));
           Assert.That(b.MInt4, Is.EqualTo(13));
           Assert.That(b.MInt5, Is.EqualTo(0));
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
            Assert.That(multiTagged.MMyCompactStruct.HasValue, Is.False);
            Assert.That(multiTagged.MAnotherCompactStruct.HasValue, Is.False);

            Assert.That(multiTagged.MByteSeq, Is.Null);
            Assert.That(multiTagged.MStringSeq, Is.Null);
            Assert.That(multiTagged.MShortSeq, Is.Null);
            Assert.That(multiTagged.MMyEnumSeq, Is.Null);
            Assert.That(multiTagged.MMyCompactStructSeq, Is.Null);
            Assert.That(multiTagged.MAnotherCompactStructSeq, Is.Null);

            Assert.That(multiTagged.MIntDict, Is.Null);
            Assert.That(multiTagged.MStringDict, Is.Null);
            Assert.That(multiTagged.MUShortSeq, Is.Null);
            Assert.That(multiTagged.MVarULongSeq, Is.Null);
            Assert.That(multiTagged.MVarIntSeq, Is.Null);

            Assert.That(multiTagged.MByteDict, Is.Null);
            Assert.That(multiTagged.MMyCompactStructDict, Is.Null);
            Assert.That(multiTagged.MAnotherCompactStructDict, Is.Null);
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
