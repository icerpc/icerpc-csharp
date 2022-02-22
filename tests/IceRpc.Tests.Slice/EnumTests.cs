// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    public class EnumTests
    {
        [Test]
        public void Enum_Values()
        {
            // Ensure the generate enum has the expected values
           Assert.That((int)MyEnum.enum1, Is.EqualTo(0));
           Assert.That((int)MyEnum.enum2, Is.EqualTo(1));
           Assert.That((int)MyEnum.enum3, Is.EqualTo(10));
           Assert.That((int)MyEnum.enum4, Is.EqualTo(11));
           Assert.That((int)MyEnum.enum5, Is.EqualTo(20));
           Assert.That((int)MyEnum.enum6, Is.EqualTo(21));
           Assert.That((int)MyEnum.enum7, Is.EqualTo(30));
           Assert.That((int)MyEnum.enum8, Is.EqualTo(31));
           Assert.That((int)MyEnum.enum9, Is.EqualTo(40));
           Assert.That((int)MyEnum.enum10, Is.EqualTo(41));
           Assert.That((int)MyEnum.enum11, Is.EqualTo(226));

           Assert.That(sizeof(MyFixedLengthEnum), Is.EqualTo(sizeof(short)));

           Assert.That((short)MyFixedLengthEnum.senum1, Is.EqualTo(-3));
           Assert.That((short)MyFixedLengthEnum.senum2, Is.EqualTo(-2));
           Assert.That((short)MyFixedLengthEnum.senum3, Is.EqualTo(10));
           Assert.That((short)MyFixedLengthEnum.senum4, Is.EqualTo(11));
           Assert.That((short)MyFixedLengthEnum.senum5, Is.EqualTo(20));
           Assert.That((short)MyFixedLengthEnum.senum6, Is.EqualTo(21));
           Assert.That((short)MyFixedLengthEnum.senum7, Is.EqualTo(30));
           Assert.That((short)MyFixedLengthEnum.senum8, Is.EqualTo(31));
           Assert.That((short)MyFixedLengthEnum.senum9, Is.EqualTo(40));
           Assert.That((short)MyFixedLengthEnum.senum10, Is.EqualTo(41));
           Assert.That((short)MyFixedLengthEnum.senum11, Is.EqualTo(32766));

           Assert.That(sizeof(MyUncheckedEnum), Is.EqualTo(sizeof(uint)));

           Assert.That((uint)MyUncheckedEnum.E0, Is.EqualTo(1));
           Assert.That((uint)MyUncheckedEnum.E1, Is.EqualTo(2));
           Assert.That((uint)MyUncheckedEnum.E2, Is.EqualTo(4));
           Assert.That((uint)MyUncheckedEnum.E3, Is.EqualTo(8));
           Assert.That((uint)MyUncheckedEnum.E4, Is.EqualTo(16));
           Assert.That((uint)MyUncheckedEnum.E5, Is.EqualTo(32));
           Assert.That((uint)MyUncheckedEnum.E6, Is.EqualTo(64));
           Assert.That((uint)MyUncheckedEnum.E7, Is.EqualTo(128));
           Assert.That((uint)MyUncheckedEnum.E8, Is.EqualTo(256));
           Assert.That((uint)MyUncheckedEnum.E9, Is.EqualTo(512));
           Assert.That((uint)MyUncheckedEnum.E10, Is.EqualTo(1024));
        }

        [Test]
        public void Enum_AsEnum()
        {
            Array myEnumValues = Enum.GetValues(typeof(MyEnum));
            foreach (object value in myEnumValues)
            {
               Assert.That(IntMyEnumExtensions.AsMyEnum((int)value), Is.EqualTo((MyEnum)value));
            }

            Array myFixedLengthEnumValues = Enum.GetValues(typeof(MyFixedLengthEnum));
            foreach (object value in myFixedLengthEnumValues)
            {
               Assert.That(ShortMyFixedLengthEnumExtensions.AsMyFixedLengthEnum((short)value), Is.EqualTo((MyFixedLengthEnum)value));
            }

            for (uint i = 0; i < 1024; ++i)
            {
               Assert.That(UintMyUncheckedEnumExtensions.AsMyUncheckedEnum(i), Is.EqualTo((MyUncheckedEnum)i));
            }

            Assert.Throws<InvalidDataException>(() => IntMyEnumExtensions.AsMyEnum(2));
            Assert.Throws<InvalidDataException>(() => IntMyEnumExtensions.AsMyEnum(12));
            Assert.Throws<InvalidDataException>(() => IntMyEnumExtensions.AsMyEnum(22));

            Assert.Throws<InvalidDataException>(() => ShortMyFixedLengthEnumExtensions.AsMyFixedLengthEnum(0));
            Assert.Throws<InvalidDataException>(() => ShortMyFixedLengthEnumExtensions.AsMyFixedLengthEnum(12));
            Assert.Throws<InvalidDataException>(() => ShortMyFixedLengthEnumExtensions.AsMyFixedLengthEnum(22));
        }

        [TestCase("ice")]
        [TestCase("icerpc")]
        public async Task Enum_OperationsAsync(string protocol)
        {
            await using ServiceProvider serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher, EnumOperations>()
                .BuildServiceProvider();
            var prx = EnumOperationsPrx.FromConnection(serviceProvider.GetRequiredService<Connection>());

            await TestAsync((p1, p2) => prx.OpMyEnumAsync(p1, p2), MyEnum.enum1, MyEnum.enum10);
            await TestAsync((p1, p2) => prx.OpMyFixedLengthEnumAsync(p1, p2),
                            MyFixedLengthEnum.senum1,
                            MyFixedLengthEnum.senum10);
            await TestAsync((p1, p2) => prx.OpMyUncheckedEnumAsync(p1, p2),
                            MyUncheckedEnum.E0 | MyUncheckedEnum.E1,
                            MyUncheckedEnum.E10 | MyUncheckedEnum.E3);

            // For checked enums receiving an invalid enumerator throws InvalidDataException
            Assert.ThrowsAsync<InvalidDataException>(async () => await prx.OpInvalidMyEnumAsync());
            Assert.ThrowsAsync<InvalidDataException>(async () => await prx.OpInvalidMyFixedLengthEnumAsync());

            // Sending an invalid value for a checked enum results in a DispatchException(InvalidData)
            var dispatchException = Assert.ThrowsAsync<DispatchException>(
                () => prx.OpMyEnumAsync((MyEnum)3, MyEnum.enum1));
            Assert.That(dispatchException!.ErrorCode, Is.EqualTo(DispatchErrorCode.InvalidData));

            dispatchException = Assert.ThrowsAsync<DispatchException>(
                () => prx.OpMyFixedLengthEnumAsync(0, MyFixedLengthEnum.senum1));
            Assert.That(dispatchException!.ErrorCode, Is.EqualTo(DispatchErrorCode.InvalidData));

            static async Task TestAsync<T>(Func<T, T, Task<(T, T)>> invoker, T p1, T p2)
            {
                (T r1, T r2) = await invoker(p1, p2);
               Assert.That(r1, Is.EqualTo(p1));
               Assert.That(r2, Is.EqualTo(p2));
            }
        }

        public class EnumOperations : Service, IEnumOperations
        {
            public ValueTask<(MyEnum R1, MyEnum R2)> OpMyEnumAsync(
                MyEnum p1,
                MyEnum p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(MyFixedLengthEnum R1, MyFixedLengthEnum R2)> OpMyFixedLengthEnumAsync(
                MyFixedLengthEnum p1,
                MyFixedLengthEnum p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(MyUncheckedEnum R1, MyUncheckedEnum R2)> OpMyUncheckedEnumAsync(
                MyUncheckedEnum p1,
                MyUncheckedEnum p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<MyEnum> OpInvalidMyEnumAsync(Dispatch dispatch, CancellationToken cancel) =>
                new((MyEnum)3);

            public ValueTask<MyFixedLengthEnum> OpInvalidMyFixedLengthEnumAsync(
                Dispatch dispatch,
                CancellationToken cancel) =>
                new(0);
        }
    }
}
