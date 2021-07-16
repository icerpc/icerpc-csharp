// Copyright (c) ZeroC, Inc. All rights reserved.

using NUnit.Framework;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Tests.CodeGeneration
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    public class EnumTests
    {
        [Test]
        public void Enum_Values()
        {
            // Ensure the generate enum has the expected values
            Assert.AreEqual(0, (int)MyEnum.enum1);
            Assert.AreEqual(1, (int)MyEnum.enum2);
            Assert.AreEqual(10, (int)MyEnum.enum3);
            Assert.AreEqual(11, (int)MyEnum.enum4);
            Assert.AreEqual(20, (int)MyEnum.enum5);
            Assert.AreEqual(21, (int)MyEnum.enum6);
            Assert.AreEqual(30, (int)MyEnum.enum7);
            Assert.AreEqual(31, (int)MyEnum.enum8);
            Assert.AreEqual(40, (int)MyEnum.enum9);
            Assert.AreEqual(41, (int)MyEnum.enum10);
            Assert.AreEqual(226, (int)MyEnum.enum11);

            Assert.AreEqual(sizeof(short), sizeof(MyFixedLengthEnum));

            Assert.AreEqual(-3, (short)MyFixedLengthEnum.senum1);
            Assert.AreEqual(-2, (short)MyFixedLengthEnum.senum2);
            Assert.AreEqual(10, (short)MyFixedLengthEnum.senum3);
            Assert.AreEqual(11, (short)MyFixedLengthEnum.senum4);
            Assert.AreEqual(20, (short)MyFixedLengthEnum.senum5);
            Assert.AreEqual(21, (short)MyFixedLengthEnum.senum6);
            Assert.AreEqual(30, (short)MyFixedLengthEnum.senum7);
            Assert.AreEqual(31, (short)MyFixedLengthEnum.senum8);
            Assert.AreEqual(40, (short)MyFixedLengthEnum.senum9);
            Assert.AreEqual(41, (short)MyFixedLengthEnum.senum10);
            Assert.AreEqual(32766, (short)MyFixedLengthEnum.senum11);

            Assert.AreEqual(sizeof(uint), sizeof(MyUncheckedEnum));

            Assert.AreEqual(1, (uint)MyUncheckedEnum.E0);
            Assert.AreEqual(2, (uint)MyUncheckedEnum.E1);
            Assert.AreEqual(4, (uint)MyUncheckedEnum.E2);
            Assert.AreEqual(8, (uint)MyUncheckedEnum.E3);
            Assert.AreEqual(16, (uint)MyUncheckedEnum.E4);
            Assert.AreEqual(32, (uint)MyUncheckedEnum.E5);
            Assert.AreEqual(64, (uint)MyUncheckedEnum.E6);
            Assert.AreEqual(128, (uint)MyUncheckedEnum.E7);
            Assert.AreEqual(256, (uint)MyUncheckedEnum.E8);
            Assert.AreEqual(512, (uint)MyUncheckedEnum.E9);
            Assert.AreEqual(1024, (uint)MyUncheckedEnum.E10);
        }

        [Test]
        public void Enum_AsEnum()
        {
            Array myEnumValues = Enum.GetValues(typeof(MyEnum));
            foreach (object value in myEnumValues)
            {
                Assert.AreEqual((MyEnum)value, MyEnumHelper.AsMyEnum((int)value));
            }

            Array myFixedLengthEnumValues = Enum.GetValues(typeof(MyFixedLengthEnum));
            foreach (object value in myFixedLengthEnumValues)
            {
                Assert.AreEqual((MyFixedLengthEnum)value, MyFixedLengthEnumHelper.AsMyFixedLengthEnum((short)value));
            }

            for (uint i = 0; i < 1024; ++i)
            {
                Assert.AreEqual((MyUncheckedEnum)i, MyUncheckedEnumHelper.AsMyUncheckedEnum(i));
            }

            Assert.Throws<InvalidDataException>(() => MyEnumHelper.AsMyEnum(2));
            Assert.Throws<InvalidDataException>(() => MyEnumHelper.AsMyEnum(12));
            Assert.Throws<InvalidDataException>(() => MyEnumHelper.AsMyEnum(22));

            Assert.Throws<InvalidDataException>(() => MyFixedLengthEnumHelper.AsMyFixedLengthEnum(0));
            Assert.Throws<InvalidDataException>(() => MyFixedLengthEnumHelper.AsMyFixedLengthEnum(12));
            Assert.Throws<InvalidDataException>(() => MyFixedLengthEnumHelper.AsMyFixedLengthEnum(22));
        }

        [TestCase(Protocol.Ice1)]
        [TestCase(Protocol.Ice2)]
        public async Task Enum_OperationsAsync(Protocol protocol)
        {
            await WithEnumOperationsServerAsync(
                protocol,
                async (prx) =>
                {
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

                    // Sending an invalid value for a checked enum results in an UnhandledException
                    Assert.ThrowsAsync<UnhandledException>(
                        async () => await prx.OpMyEnumAsync((MyEnum)3, MyEnum.enum1));
                    Assert.ThrowsAsync<UnhandledException>(
                    async () => await prx.OpMyFixedLengthEnumAsync(0, MyFixedLengthEnum.senum1));
                });

            static async Task TestAsync<T>(Func<T, T, Task<(T, T)>> invoker, T p1, T p2)
            {
                (T r1, T r2) = await invoker(p1, p2);
                Assert.AreEqual(p1, r1);
                Assert.AreEqual(p2, r2);
            }
        }

        private static async Task WithEnumOperationsServerAsync(
            Protocol protocol,
            Func<IEnumOperationsPrx, Task> closure)
        {
            await using var server = new Server
            {
                Dispatcher = new EnumOperations(),
                Endpoint = TestHelper.GetUniqueColocEndpoint(protocol)
            };
            server.Listen();
            await using var connection = new Connection { RemoteEndpoint = server.ProxyEndpoint };
            IEnumOperationsPrx prx = EnumOperationsPrx.FromConnection(connection);
            Assert.AreEqual(protocol, prx.Protocol);
            await closure(prx);
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
