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

            multiTagged.MUInt8 = 1;
            multiTagged.MInt16 = 1;
            multiTagged.MInt64 = 1;
            multiTagged.MFloat64 = 1.0;
            multiTagged.MString = "1";
            // TODO: These types are Slice2 only. We should either delete these or create our own copy for this test.
            // multiTagged.MMyEnum = MyEnum.enum1;
            // multiTagged.MAnotherCompactStruct = new AnotherCompactStruct(
            //     "hello",
            //     OperationsPrx.Parse("icerpc://localhost/hello"),
            //     MyEnum.enum1,
            //     new MyCompactStruct(1, 1));

            // multiTagged.MStringSeq = new string[] { "hello" };
            // multiTagged.MMyEnumSeq = new MyEnum[] { MyEnum.enum1 };
            // multiTagged.MAnotherCompactStructSeq = new AnotherCompactStruct[] { multiTagged.MAnotherCompactStruct.Value };

            // multiTagged.MStringDict = new Dictionary<string, string>()
            // {
            //     { "key", "value" }
            // };
            // multiTagged.MVarInt32Seq = new int[] { 1 };

            // multiTagged.MUInt8Dict = new Dictionary<byte, byte>() { { 1, 1 } };
            // multiTagged.MAnotherCompactStructDict = new Dictionary<string, AnotherCompactStruct>()
            // {
            //     { "key", multiTagged.MAnotherCompactStruct.Value}
            // };

            var multiTagged1 = (MultiTagged)await _prx.PingPongAsync(multiTagged);
            Assert.That(multiTagged1.MUInt8, Is.EqualTo(multiTagged.MUInt8));
            Assert.That(multiTagged1.MBool, Is.EqualTo(multiTagged.MBool));
            Assert.That(multiTagged1.MInt16, Is.EqualTo(multiTagged.MInt16));
            Assert.That(multiTagged1.MInt32, Is.EqualTo(multiTagged.MInt32));
            Assert.That(multiTagged1.MInt64, Is.EqualTo(multiTagged.MInt64));
            Assert.That(multiTagged1.MFloat32, Is.EqualTo(multiTagged.MFloat32));
            Assert.That(multiTagged1.MFloat64, Is.EqualTo(multiTagged.MFloat64));
            // Assert.That(multiTagged1.MUInt16, Is.EqualTo(multiTagged.MUInt16));
            // Assert.That(multiTagged1.MUInt32, Is.EqualTo(multiTagged.MUInt32));
            // Assert.That(multiTagged1.MUInt64, Is.EqualTo(multiTagged.MUInt64));
            Assert.That(multiTagged1.MString, Is.EqualTo(multiTagged.MString));
            // Assert.That(multiTagged1.MMyEnum, Is.EqualTo(multiTagged.MMyEnum));
            // Assert.That(multiTagged1.MMyCompactStruct, Is.EqualTo(multiTagged.MMyCompactStruct));
            // Assert.That(multiTagged1.MAnotherCompactStruct, Is.EqualTo(multiTagged.MAnotherCompactStruct));

            // Assert.That(multiTagged1.MUInt8Seq, Is.Null);
            // Assert.That(multiTagged1.MStringSeq, Is.EqualTo(multiTagged.MStringSeq));
            // Assert.That(multiTagged1.MInt16Seq, Is.Null);
            // Assert.That(multiTagged1.MMyEnumSeq, Is.EqualTo(multiTagged.MMyEnumSeq));
            // Assert.That(multiTagged1.MMyCompactStructSeq, Is.Null);
            // Assert.That(multiTagged1.MAnotherCompactStructSeq, Is.EqualTo(multiTagged.MAnotherCompactStructSeq));

            // Assert.That(multiTagged1.MInt32Dict, Is.Null);
            // Assert.That(multiTagged1.MStringDict, Is.EqualTo(multiTagged.MStringDict));
            // Assert.That(multiTagged1.MUInt16Seq, Is.Null);
            // Assert.That(multiTagged1.MVarUInt62Seq, Is.Null);
            // Assert.That(multiTagged1.MVarInt32Seq, Is.EqualTo(multiTagged.MVarInt32Seq));

            // Assert.That(multiTagged1.MUInt8Dict, Is.EqualTo(multiTagged.MUInt8Dict));
            // Assert.That(multiTagged1.MMyCompactStructDict, Is.Null);
            // Assert.That(multiTagged1.MAnotherCompactStructDict, Is.EqualTo(multiTagged.MAnotherCompactStructDict));

            multiTagged = new MultiTagged();
            multiTagged.MBool = true;
            multiTagged.MInt32 = 1;
            multiTagged.MFloat32 = 1;
            // multiTagged.MUInt16 = 1;
            // multiTagged.MMyEnum = MyEnum.enum1;
            // multiTagged.MMyCompactStruct = new MyCompactStruct(1, 1);

            // multiTagged.MUInt8Seq = new byte[] { 1 };
            // multiTagged.MInt16Seq = new short[] { 1 };
            // multiTagged.MMyCompactStructSeq = new MyCompactStruct[] { new MyCompactStruct(1, 1) };

            // multiTagged.MInt32Dict = new Dictionary<int, int> { { 1, 1 } };
            // multiTagged.MUInt16Seq = new ushort[] { 1 };
            // multiTagged.MVarInt32Seq = new int[] { 1 };
            // multiTagged.MMyCompactStructDict = new Dictionary<MyCompactStruct, MyCompactStruct>()
            // {
            //     { new MyCompactStruct(1, 1), new MyCompactStruct(1, 1) }
            // };

            multiTagged1 = (MultiTagged)await _prx.PingPongAsync(multiTagged);
            Assert.That(multiTagged1.MUInt8, Is.EqualTo(multiTagged.MUInt8));
            Assert.That(multiTagged1.MBool, Is.EqualTo(multiTagged.MBool));
            Assert.That(multiTagged1.MInt16, Is.EqualTo(multiTagged.MInt16));
            Assert.That(multiTagged1.MInt32, Is.EqualTo(multiTagged.MInt32));
            Assert.That(multiTagged1.MInt64, Is.EqualTo(multiTagged.MInt64));
            Assert.That(multiTagged1.MFloat32, Is.EqualTo(multiTagged.MFloat32));
            Assert.That(multiTagged1.MFloat64, Is.EqualTo(multiTagged.MFloat64));
            // Assert.That(multiTagged1.MUInt16, Is.EqualTo(multiTagged.MUInt16));
            // Assert.That(multiTagged1.MUInt32, Is.EqualTo(multiTagged.MUInt32));
            // Assert.That(multiTagged1.MUInt64, Is.EqualTo(multiTagged.MUInt64));
            // Assert.That(multiTagged1.MVarInt32, Is.EqualTo(multiTagged.MVarInt32));
            // Assert.That(multiTagged1.MVarInt62, Is.EqualTo(multiTagged.MVarInt62));
            // Assert.That(multiTagged1.MVarUInt32, Is.EqualTo(multiTagged.MVarUInt32));
            // Assert.That(multiTagged1.MVarUInt62, Is.EqualTo(multiTagged.MVarUInt62));
            Assert.That(multiTagged1.MString, Is.EqualTo(multiTagged.MString));
            // Assert.That(multiTagged1.MMyEnum, Is.EqualTo(multiTagged.MMyEnum));
            // Assert.That(multiTagged1.MMyCompactStruct, Is.EqualTo(multiTagged.MMyCompactStruct));
            // Assert.That(multiTagged1.MAnotherCompactStruct, Is.EqualTo(multiTagged.MAnotherCompactStruct));

            // Assert.That(multiTagged1.MUInt8Seq, Is.EqualTo(multiTagged.MUInt8Seq));
            // Assert.That(multiTagged1.MStringSeq, Is.Null);
            // Assert.That(multiTagged1.MInt16Seq, Is.EqualTo(multiTagged.MInt16Seq));
            // Assert.That(multiTagged1.MMyEnumSeq, Is.Null);
            // Assert.That(multiTagged1.MMyCompactStructSeq, Is.EqualTo(multiTagged.MMyCompactStructSeq));
            // Assert.That(multiTagged1.MAnotherCompactStructSeq, Is.Null);

            // Assert.That(multiTagged1.MInt32Dict, Is.EqualTo(multiTagged.MInt32Dict));
            // Assert.That(multiTagged1.MStringDict, Is.Null);
            // Assert.That(multiTagged1.MUInt16Seq, Is.EqualTo(multiTagged.MUInt16Seq));
            // Assert.That(multiTagged1.MVarUInt62Seq, Is.Null);
            // Assert.That(multiTagged1.MVarInt32Seq, Is.EqualTo(multiTagged.MVarInt32Seq));

            // Assert.That(multiTagged1.MUInt8Dict, Is.Null);
            // Assert.That(multiTagged1.MMyCompactStructDict, Is.EqualTo(multiTagged.MMyCompactStructDict));
            // Assert.That(multiTagged1.MAnotherCompactStructDict, Is.Null);

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
            Assert.That(multiTagged.MUInt8.HasValue, Is.False);
            Assert.That(multiTagged.MBool.HasValue, Is.False);
            Assert.That(multiTagged.MInt16.HasValue, Is.False);
            Assert.That(multiTagged.MInt32.HasValue, Is.False);
            Assert.That(multiTagged.MInt64.HasValue, Is.False);
            Assert.That(multiTagged.MFloat32.HasValue, Is.False);
            Assert.That(multiTagged.MFloat64.HasValue, Is.False);
            // Assert.That(multiTagged.MUInt16.HasValue, Is.False);
            // Assert.That(multiTagged.MUInt32.HasValue, Is.False);
            // Assert.That(multiTagged.MUInt64.HasValue, Is.False);
            // Assert.That(multiTagged.MVarInt32.HasValue, Is.False);
            // Assert.That(multiTagged.MVarInt62.HasValue, Is.False);
            // Assert.That(multiTagged.MVarUInt32.HasValue, Is.False);
            // Assert.That(multiTagged.MVarUInt62.HasValue, Is.False);
            Assert.That(multiTagged.MString, Is.Null);
            // Assert.That(multiTagged.MMyEnum.HasValue, Is.False);
            // Assert.That(multiTagged.MMyCompactStruct.HasValue, Is.False);
            // Assert.That(multiTagged.MAnotherCompactStruct.HasValue, Is.False);

            // Assert.That(multiTagged.MUInt8Seq, Is.Null);
            // Assert.That(multiTagged.MStringSeq, Is.Null);
            // Assert.That(multiTagged.MInt16Seq, Is.Null);
            // Assert.That(multiTagged.MMyEnumSeq, Is.Null);
            // Assert.That(multiTagged.MMyCompactStructSeq, Is.Null);
            // Assert.That(multiTagged.MAnotherCompactStructSeq, Is.Null);

            // Assert.That(multiTagged.MInt32Dict, Is.Null);
            // Assert.That(multiTagged.MStringDict, Is.Null);
            // Assert.That(multiTagged.MUInt16Seq, Is.Null);
            // Assert.That(multiTagged.MVarUInt62Seq, Is.Null);
            // Assert.That(multiTagged.MVarInt32Seq, Is.Null);

            // Assert.That(multiTagged.MUInt8Dict, Is.Null);
            // Assert.That(multiTagged.MMyCompactStructDict, Is.Null);
            // Assert.That(multiTagged.MAnotherCompactStructDict, Is.Null);
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
