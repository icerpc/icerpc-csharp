// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
    [Parallelizable(ParallelScope.All)]
    public class EnumTests
    {
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
