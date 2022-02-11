// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("icerpc")]
    public sealed class SequenceOptionalElementTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly SequenceOptionalElementOperationsPrx _prx;

        public SequenceOptionalElementTests(string protocol)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher, SequenceOperations>()
                .BuildServiceProvider();
            _prx = SequenceOptionalElementOperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task Sequence_OptionalBuiltinTypesAsync()
        {
            int size = 100;
            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalByteSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (byte?)i : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (byte?)i : null).ToArray());

            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalBoolSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (bool?)true : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (bool?)false : null).ToArray());

            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalShortSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (short?)i : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (short?)i : null).ToArray());

            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalUShortSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (ushort?)i : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (ushort?)i : null).ToArray());

            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalIntSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (int?)i : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (int?)i : null).ToArray());

            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalVarIntSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (int?)i : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (int?)i : null).ToArray());

            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalUIntSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (uint?)i : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (uint?)i : null).ToArray());

            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalVarUIntSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (uint?)i : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (uint?)i : null).ToArray());

            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalLongSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (long?)i : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (long?)i : null).ToArray());

            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalVarLongSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (long?)i : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (long?)i : null).ToArray());

            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalULongSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (ulong?)i : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (ulong?)i : null).ToArray());

            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalVarULongSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (ulong?)i : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (ulong?)i : null).ToArray());

            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalFloatSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (float?)i : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (float?)i : null).ToArray());

            await TestOptionalSeqAsync(
                (p1, p2) => _prx.OpOptionalDoubleSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (double?)i : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (double?)i : null).ToArray());

            await TestOptionalSeqAsync(
               (p1, p2) => _prx.OpOptionalStringSeqAsync(p1, p2),
               Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (string?)$"string-{i}" : null).ToArray(),
               Enumerable.Range(0, size).Select(i => i % 2 == 0 ? (string?)$"string-{i}" : null).ToArray());
        }

        [Test]
        public async Task Sequence_OptionalDefinedTypesAsync()
        {
            int size = 100;

            Array myEnumValues = Enum.GetValues(typeof(MyEnum));
            await TestEnumerableSeqAsync(
                (p1, p2) => _prx.OpOptionalMyEnumSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(
                    i => i % 2 == 0 ? (MyEnum?)GetEnum<MyEnum>(myEnumValues, i) : null).ToArray(),
                Enumerable.Range(0, size).Select(
                    i => i % 2 == 0 ? (MyEnum?)GetEnum<MyEnum>(myEnumValues, i) : null).ToArray());

            Array myFixedLengthEnumValues = Enum.GetValues(typeof(MyFixedLengthEnum));
            await TestEnumerableSeqAsync(
                (p1, p2) => _prx.OpOptionalMyFixedLengthEnumSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(
                    i => i % 2 == 0 ?
                        (MyFixedLengthEnum?)GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i) : null).ToArray(),
                Enumerable.Range(0, size).Select(
                    i => i % 2 == 0 ?
                        (MyFixedLengthEnum?)GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i) : null).ToArray());

            await TestEnumerableSeqAsync(
                (p1, p2) => _prx.OpOptionalMyCompactStructSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(
                    i => i % 2 == 0 ? (MyCompactStruct?)new MyCompactStruct(i, i + 1) : null).ToArray(),
                Enumerable.Range(0, size).Select(
                    i => i % 2 == 0 ? (MyCompactStruct?)new MyCompactStruct(i, i + 1) : null).ToArray());

            await TestEnumerableSeqAsync(
                (p1, p2) => _prx.OpOptionalOperationsSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => i == 0 ? (OperationsPrx?)GetOperationsPrx(i) : null).ToArray(),
                Enumerable.Range(0, size).Select(i => i == 0 ? (OperationsPrx?)GetOperationsPrx(i) : null).ToArray());

            await TestEnumerableSeqAsync(
                (p1, p2) => _prx.OpOptionalAnotherCompactStructSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(
                    i => i % 2 == 0 ? (AnotherCompactStruct?)GetAnotherCompactStruct(myEnumValues, i) : null).ToArray(),
                Enumerable.Range(0, size).Select(
                    i => i % 2 == 0 ? (AnotherCompactStruct?)GetAnotherCompactStruct(myEnumValues, i) : null).ToArray());
        }

        public class SequenceOperations : Service, ISequenceOptionalElementOperations
        {
            // Optional builtin type sequences

            public ValueTask<(IEnumerable<byte?> R1, IEnumerable<byte?> R2)> OpOptionalByteSeqAsync(
                byte?[] p1,
                byte?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<bool?> R1, IEnumerable<bool?> R2)> OpOptionalBoolSeqAsync(
                bool?[] p1,
                bool?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<short?> R1, IEnumerable<short?> R2)> OpOptionalShortSeqAsync(
                short?[] p1,
                short?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<ushort?> R1, IEnumerable<ushort?> R2)> OpOptionalUShortSeqAsync(
                ushort?[] p1,
                ushort?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<int?> R1, IEnumerable<int?> R2)> OpOptionalIntSeqAsync(
                int?[] p1,
                int?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<int?> R1, IEnumerable<int?> R2)> OpOptionalVarIntSeqAsync(
                int?[] p1,
                int?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<uint?> R1, IEnumerable<uint?> R2)> OpOptionalUIntSeqAsync(
                uint?[] p1,
                uint?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<uint?> R1, IEnumerable<uint?> R2)> OpOptionalVarUIntSeqAsync(
                uint?[] p1,
                uint?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<long?> R1, IEnumerable<long?> R2)> OpOptionalLongSeqAsync(
                long?[] p1,
                long?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<long?> R1, IEnumerable<long?> R2)> OpOptionalVarLongSeqAsync(
                long?[] p1,
                long?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<ulong?> R1, IEnumerable<ulong?> R2)> OpOptionalULongSeqAsync(
                ulong?[] p1,
                ulong?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<ulong?> R1, IEnumerable<ulong?> R2)> OpOptionalVarULongSeqAsync(
                ulong?[] p1,
                ulong?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<float?> R1, IEnumerable<float?> R2)> OpOptionalFloatSeqAsync(
                float?[] p1,
                float?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<double?> R1, IEnumerable<double?> R2)> OpOptionalDoubleSeqAsync(
                double?[] p1,
                double?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<string?> R1, IEnumerable<string?> R2)> OpOptionalStringSeqAsync(
                string?[] p1,
                string?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            // Optional defined types sequences
            public ValueTask<(IEnumerable<MyEnum?> R1, IEnumerable<MyEnum?> R2)> OpOptionalMyEnumSeqAsync(
                MyEnum?[] p1,
                MyEnum?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyFixedLengthEnum?> R1, IEnumerable<MyFixedLengthEnum?> R2)> OpOptionalMyFixedLengthEnumSeqAsync(
                MyFixedLengthEnum?[] p1,
                MyFixedLengthEnum?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyUncheckedEnum?> R1, IEnumerable<MyUncheckedEnum?> R2)> OpOptionalMyUncheckedEnumSeqAsync(
                MyUncheckedEnum?[] p1,
                MyUncheckedEnum?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyCompactStruct?> R1, IEnumerable<MyCompactStruct?> R2)> OpOptionalMyCompactStructSeqAsync(
                MyCompactStruct?[] p1,
                MyCompactStruct?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<OperationsPrx?> R1, IEnumerable<OperationsPrx?> R2)> OpOptionalOperationsSeqAsync(
                OperationsPrx?[] p1,
                OperationsPrx?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<AnotherCompactStruct?> R1, IEnumerable<AnotherCompactStruct?> R2)> OpOptionalAnotherCompactStructSeqAsync(
                AnotherCompactStruct?[] p1,
                AnotherCompactStruct?[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));
        }

        private static async Task TestSeqAsync<T>(
            Func<ReadOnlyMemory<T>, ReadOnlyMemory<T>, Task<(T[], T[])>> invoker,
            T[] p1,
            T[] p2)
        {
            (T[] r1, T[] r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(r1, p1);
            CollectionAssert.AreEqual(r2, p2);
        }

        private static async Task TestOptionalSeqAsync<T>(
            Func<IEnumerable<T>, IEnumerable<T>, Task<(T[], T[])>> invoker,
            T[] p1,
            T[] p2)
        {
            (T[] r1, T[] r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(r1, p1);
            CollectionAssert.AreEqual(r2, p2);
        }

        private static async Task TestEnumerableSeqAsync<T>(
            Func<IEnumerable<T>, IEnumerable<T>, Task<(T[], T[])>> invoker,
            T[] p1,
            T[] p2)
        {
            (T[] r1, T[] r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(r1, p1);
            CollectionAssert.AreEqual(r2, p2);
        }

        private static async Task TestReadOnlyMemorySeqAsync<T>(
            Func<ReadOnlyMemory<T>, ReadOnlyMemory<T>, Task<(T[], T[])>> invoker,
            T[] p1,
            T[] p2)
        {
            (T[] r1, T[] r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(p1, r1);
            CollectionAssert.AreEqual(p2, r2);
        }

        private static async Task TestListAsync<T>(
                Func<List<T>, List<T>, Task<(List<T>, List<T>)>> invoker,
                List<T> p1,
                List<T> p2)
        {
            (List<T> r1, List<T> r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(r1, p1);
            CollectionAssert.AreEqual(r2, p2);
        }

        private static async Task TestCustomSeqAsync<T>(
            Func<CustomSequence<T>, CustomSequence<T>, Task<(CustomSequence<T>, CustomSequence<T>)>> invoker,
            CustomSequence<T> p1,
            CustomSequence<T> p2)
        {
            (CustomSequence<T> r1, CustomSequence<T> r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(r1, p1);
            CollectionAssert.AreEqual(r2, p2);
        }

        private static T GetEnum<T>(Array values, int i) => (T)values.GetValue(i % values.Length)!;

        private static OperationsPrx GetOperationsPrx(int i) => OperationsPrx.Parse($"icerpc://host/foo-{i}");

        private static AnotherCompactStruct GetAnotherCompactStruct(Array myEnumValues, int i)
        {
            return new AnotherCompactStruct($"hello-{i}",
                                     GetOperationsPrx(i),
                                     (MyEnum)myEnumValues.GetValue(i % myEnumValues.Length)!,
                                     new MyCompactStruct(i, i + 1));
        }
    }
}
