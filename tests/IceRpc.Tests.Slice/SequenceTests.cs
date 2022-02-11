// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("ice")]
    [TestFixture("icerpc")]
    public sealed class SequenceTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly SequenceOperationsPrx _prx;

        public SequenceTests(string protocol)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher, SequenceOperations>()
                .BuildServiceProvider();
            _prx = SequenceOperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task Sequence_BuiltinTypesAsync()
        {
            int size = 100;
            await TestSeqAsync((p1, p2) => _prx.OpByteSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (byte)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (byte)i).ToArray());
            await TestListAsync((p1, p2) => _prx.OpByteListAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => (byte)i).ToList(),
                                Enumerable.Range(0, size).Select(i => (byte)i).ToList());
            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpByteCustomSeqAsync(p1, p2),
                new CustomSequence<byte>(Enumerable.Range(0, size).Select(i => (byte)i)),
                new CustomSequence<byte>(Enumerable.Range(0, size).Select(i => (byte)i).ToList()));

            await TestSeqAsync((p1, p2) => _prx.OpBoolSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => i % 2 == 0).ToArray(),
                               Enumerable.Range(0, size).Select(i => i % 2 == 0).ToArray());
            await TestListAsync((p1, p2) => _prx.OpBoolListAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => i % 2 == 0).ToList(),
                                Enumerable.Range(0, size).Select(i => i % 2 == 0).ToList());
            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpBoolCustomSeqAsync(p1, p2),
                new CustomSequence<bool>(Enumerable.Range(0, size).Select(i => i % 2 == 0)),
                new CustomSequence<bool>(Enumerable.Range(0, size).Select(i => i % 2 == 0).ToList()));

            await TestSeqAsync((p1, p2) => _prx.OpShortSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (short)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (short)i).ToArray());

            await TestSeqAsync((p1, p2) => _prx.OpUShortSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (ushort)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (ushort)i).ToArray());

            await TestSeqAsync((p1, p2) => _prx.OpIntSeqAsync(p1, p2),
                               Enumerable.Range(0, size).ToArray(),
                               Enumerable.Range(0, size).ToArray());

            await TestEnumerableSeqAsync((p1, p2) => _prx.OpVarIntSeqAsync(p1, p2),
                                         Enumerable.Range(0, size).ToArray(),
                                         Enumerable.Range(0, size).ToArray());

            await TestListAsync((p1, p2) => _prx.OpIntListAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => i).ToList(),
                                Enumerable.Range(0, size).Select(i => i).ToList());
            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpIntCustomSeqAsync(p1, p2),
                new CustomSequence<int>(Enumerable.Range(0, size).Select(i => i)),
                new CustomSequence<int>(Enumerable.Range(0, size).Select(i => i).ToList()));

            await TestSeqAsync((p1, p2) => _prx.OpUIntSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (uint)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (uint)i).ToArray());
            await TestEnumerableSeqAsync((p1, p2) => _prx.OpVarUIntSeqAsync(p1, p2),
                                         Enumerable.Range(0, size).Select(i => (uint)i).ToArray(),
                                         Enumerable.Range(0, size).Select(i => (uint)i).ToArray());

            await TestSeqAsync((p1, p2) => _prx.OpLongSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (long)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (long)i).ToArray());

            await TestListAsync((p1, p2) => _prx.OpLongListAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => (long)i).ToList(),
                                Enumerable.Range(0, size).Select(i => (long)i).ToList());

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpLongCustomSeqAsync(p1, p2),
                new CustomSequence<long>(Enumerable.Range(0, size).Select(i => (long)i)),
                new CustomSequence<long>(Enumerable.Range(0, size).Select(i => (long)i).ToList()));

            await TestEnumerableSeqAsync((p1, p2) => _prx.OpVarLongSeqAsync(p1, p2),
                                         Enumerable.Range(0, size).Select(i => (long)i).ToArray(),
                                         Enumerable.Range(0, size).Select(i => (long)i).ToArray());

            await TestSeqAsync((p1, p2) => _prx.OpULongSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (ulong)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (ulong)i).ToArray());

            await TestEnumerableSeqAsync((p1, p2) => _prx.OpVarULongSeqAsync(p1, p2),
                                         Enumerable.Range(0, size).Select(i => (ulong)i).ToArray(),
                                         Enumerable.Range(0, size).Select(i => (ulong)i).ToArray());

            await TestSeqAsync((p1, p2) => _prx.OpFloatSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (float)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (float)i).ToArray());

            await TestListAsync((p1, p2) => _prx.OpFloatListAsync(p1, p2),
                                Enumerable.Range(0, size).Select(i => (float)i).ToList(),
                                Enumerable.Range(0, size).Select(i => (float)i).ToList());

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpFloatCustomSeqAsync(p1, p2),
                new CustomSequence<float>(Enumerable.Range(0, size).Select(i => (float)i)),
                new CustomSequence<float>(Enumerable.Range(0, size).Select(i => (float)i).ToList()));

            await TestSeqAsync((p1, p2) => _prx.OpDoubleSeqAsync(p1, p2),
                               Enumerable.Range(0, size).Select(i => (double)i).ToArray(),
                               Enumerable.Range(0, size).Select(i => (double)i).ToArray());

            await TestEnumerableSeqAsync((p1, p2) => _prx.OpStringSeqAsync(p1, p2),
                                         Enumerable.Range(0, size).Select(i => $"hello-{i}").ToArray(),
                                         Enumerable.Range(0, size).Select(i => $"hello-{i}").ToArray());
        }

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
        public async Task Sequence_DefinedTypesAsync()
        {
            int size = 100;
            Array myEnumValues = Enum.GetValues(typeof(MyEnum));

            await TestEnumerableSeqAsync(
                (p1, p2) => _prx.OpMyEnumSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i)).ToArray(),
                Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i)).ToArray());

            await TestListAsync(
                (p1, p2) => _prx.OpMyEnumListAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i)).ToList(),
                Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i)).ToList());

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpMyEnumCustomSeqAsync(p1, p2),
                new CustomSequence<MyEnum>(Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i))),
                new CustomSequence<MyEnum>(Enumerable.Range(0, size).Select(i => GetEnum<MyEnum>(myEnumValues, i))));

            Array myFixedLengthEnumValues = Enum.GetValues(typeof(MyFixedLengthEnum));
            await TestReadOnlyMemorySeqAsync(
                (p1, p2) => _prx.OpMyFixedLengthEnumSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(
                    i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i)).ToArray(),
                Enumerable.Range(0, size).Select(
                    i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i)).ToArray());

            await TestListAsync(
                (p1, p2) => _prx.OpMyFixedLengthEnumListAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i)).ToList(),
                Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i)).ToList());

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpMyFixedLengthEnumCustomSeqAsync(p1, p2),
                new CustomSequence<MyFixedLengthEnum>(
                    Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))),
                new CustomSequence<MyFixedLengthEnum>(
                    Enumerable.Range(0, size).Select(i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))));

            await TestReadOnlyMemorySeqAsync(
                (p1, p2) => _prx.OpMyUncheckedEnumSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i).ToArray(),
                Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i).ToArray());

            await TestListAsync(
                (p1, p2) => _prx.OpMyUncheckedEnumListAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i).ToList(),
                Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i).ToList());

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpMyUncheckedEnumCustomSeqAsync(p1, p2),
                new CustomSequence<MyUncheckedEnum>(
                    Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i)),
                new CustomSequence<MyUncheckedEnum>(
                    Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i)));

            await TestEnumerableSeqAsync(
                (p1, p2) => _prx.OpMyCompactStructSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => new MyCompactStruct(i, i + 1)).ToArray(),
                Enumerable.Range(0, size).Select(i => new MyCompactStruct(i, i + 1)).ToArray());

            await TestListAsync(
                (p1, p2) => _prx.OpMyCompactStructListAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => new MyCompactStruct(i, i + 1)).ToList(),
                Enumerable.Range(0, size).Select(i => new MyCompactStruct(i, i + 1)).ToList());

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpMyCompactStructCustomSeqAsync(p1, p2),
                new CustomSequence<MyCompactStruct>(
                    Enumerable.Range(0, size).Select(i => new MyCompactStruct(i, i + 1))),
                new CustomSequence<MyCompactStruct>(
                    Enumerable.Range(0, size).Select(i => new MyCompactStruct(i, i + 1))));

            await TestEnumerableSeqAsync(
                (p1, p2) => _prx.OpAnotherCompactStructSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetAnotherCompactStruct(myEnumValues, i)).ToArray(),
                Enumerable.Range(0, size).Select(i => GetAnotherCompactStruct(myEnumValues, i)).ToArray());

            await TestListAsync(
                (p1, p2) => _prx.OpAnotherCompactStructListAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetAnotherCompactStruct(myEnumValues, i)).ToList(),
                Enumerable.Range(0, size).Select(i => GetAnotherCompactStruct(myEnumValues, i)).ToList());

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpAnotherCompactStructCustomSeqAsync(p1, p2),
                new CustomSequence<AnotherCompactStruct>(
                    Enumerable.Range(0, size).Select(i => GetAnotherCompactStruct(myEnumValues, i))),
                new CustomSequence<AnotherCompactStruct>(
                    Enumerable.Range(0, size).Select(i => GetAnotherCompactStruct(myEnumValues, i))));

            await TestEnumerableSeqAsync(
                (p1, p2) => _prx.OpOperationsSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetOperationsPrx(i)).ToArray(),
                Enumerable.Range(0, size).Select(i => GetOperationsPrx(i)).ToArray());

            await TestListAsync(
                (p1, p2) => _prx.OpOperationsListAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => GetOperationsPrx(i)).ToList(),
                Enumerable.Range(0, size).Select(i => GetOperationsPrx(i)).ToList());

            await TestCustomSeqAsync(
                (p1, p2) => _prx.OpOperationsCustomSeqAsync(p1, p2),
                new CustomSequence<OperationsPrx>(Enumerable.Range(0, size).Select(i => GetOperationsPrx(i))),
                new CustomSequence<OperationsPrx>(Enumerable.Range(0, size).Select(i => GetOperationsPrx(i))));
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

            Array myUncheckedEnumValues = Enum.GetValues(typeof(MyFixedLengthEnum));
            await TestReadOnlyMemorySeqAsync(
                (p1, p2) => _prx.OpMyUncheckedEnumSeqAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i).ToArray(),
                Enumerable.Range(0, size).Select(i => (MyUncheckedEnum)i).ToArray());

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

        public class SequenceOperations : Service, ISequenceOperations
        {
            // Builtin type sequences

            public ValueTask<(ReadOnlyMemory<byte> R1, ReadOnlyMemory<byte> R2)> OpByteSeqAsync(
                byte[] p1,
                byte[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<bool> R1, ReadOnlyMemory<bool> R2)> OpBoolSeqAsync(
                bool[] p1,
                bool[] p2,
                Dispatch dispatch, CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<short> R1, ReadOnlyMemory<short> R2)> OpShortSeqAsync(
                short[] p1,
                short[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<ushort> R1, ReadOnlyMemory<ushort> R2)> OpUShortSeqAsync(
                ushort[] p1,
                ushort[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<int> R1, ReadOnlyMemory<int> R2)> OpIntSeqAsync(
                int[] p1,
                int[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<int> R1, IEnumerable<int> R2)> OpVarIntSeqAsync(
                int[] p1,
                int[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<uint> R1, ReadOnlyMemory<uint> R2)> OpUIntSeqAsync(
                uint[] p1,
                uint[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<uint> R1, IEnumerable<uint> R2)> OpVarUIntSeqAsync(
                uint[] p1,
                uint[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<long> R1, ReadOnlyMemory<long> R2)> OpLongSeqAsync(
                long[] p1,
                long[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<long> R1, IEnumerable<long> R2)> OpVarLongSeqAsync(
                long[] p1,
                long[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<ulong> R1, ReadOnlyMemory<ulong> R2)> OpULongSeqAsync(
                ulong[] p1,
                ulong[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<ulong> R1, IEnumerable<ulong> R2)> OpVarULongSeqAsync(
                ulong[] p1,
                ulong[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<float> R1, ReadOnlyMemory<float> R2)> OpFloatSeqAsync(
                float[] p1,
                float[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<double> R1, ReadOnlyMemory<double> R2)> OpDoubleSeqAsync(
                double[] p1,
                double[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<string> R1, IEnumerable<string> R2)> OpStringSeqAsync(
                string[] p1,
                string[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

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

            // Defined types sequences
            public ValueTask<(IEnumerable<MyEnum> R1, IEnumerable<MyEnum> R2)> OpMyEnumSeqAsync(
                MyEnum[] p1,
                MyEnum[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<MyFixedLengthEnum> R1, ReadOnlyMemory<MyFixedLengthEnum> R2)> OpMyFixedLengthEnumSeqAsync(
                MyFixedLengthEnum[] p1,
                MyFixedLengthEnum[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(ReadOnlyMemory<MyUncheckedEnum> R1, ReadOnlyMemory<MyUncheckedEnum> R2)> OpMyUncheckedEnumSeqAsync(
                MyUncheckedEnum[] p1,
                MyUncheckedEnum[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyCompactStruct> R1, IEnumerable<MyCompactStruct> R2)> OpMyCompactStructSeqAsync(
                MyCompactStruct[] p1,
                MyCompactStruct[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<OperationsPrx> R1, IEnumerable<OperationsPrx> R2)> OpOperationsSeqAsync(
                OperationsPrx[] p1,
                OperationsPrx[] p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<AnotherCompactStruct> R1, IEnumerable<AnotherCompactStruct> R2)> OpAnotherCompactStructSeqAsync(
                AnotherCompactStruct[] p1,
                AnotherCompactStruct[] p2,
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

            // Sequence mapping
            public ValueTask<(IEnumerable<byte> R1, IEnumerable<byte> R2)> OpByteListAsync(
                List<byte> p1,
                List<byte> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<byte> R1, IEnumerable<byte> R2)> OpByteCustomSeqAsync(
                CustomSequence<byte> p1,
                CustomSequence<byte> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<bool> R1, IEnumerable<bool> R2)> OpBoolListAsync(
                List<bool> p1,
                List<bool> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<bool> R1, IEnumerable<bool> R2)> OpBoolCustomSeqAsync(
                CustomSequence<bool> p1,
                CustomSequence<bool> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<int> R1, IEnumerable<int> R2)> OpIntListAsync(
                List<int> p1,
                List<int> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<int> R1, IEnumerable<int> R2)> OpIntCustomSeqAsync(
                CustomSequence<int> p1,
                CustomSequence<int> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<long> R1, IEnumerable<long> R2)> OpLongListAsync(
                List<long> p1,
                List<long> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<long> R1, IEnumerable<long> R2)> OpLongCustomSeqAsync(
                CustomSequence<long> p1,
                CustomSequence<long> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<float> R1, IEnumerable<float> R2)> OpFloatListAsync(
                List<float> p1,
                List<float> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<float> R1, IEnumerable<float> R2)> OpFloatCustomSeqAsync(
                CustomSequence<float> p1,
                CustomSequence<float> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<string> R1, IEnumerable<string> R2)> OpStringListAsync(
                List<string> p1,
                List<string> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<string> R1, IEnumerable<string> R2)> OpStringCustomSeqAsync(
                CustomSequence<string> p1,
                CustomSequence<string> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyEnum> R1, IEnumerable<MyEnum> R2)> OpMyEnumListAsync(
                List<MyEnum> p1,
                List<MyEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyEnum> R1, IEnumerable<MyEnum> R2)> OpMyEnumCustomSeqAsync(
                CustomSequence<MyEnum> p1,
                CustomSequence<MyEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyFixedLengthEnum> R1, IEnumerable<MyFixedLengthEnum> R2)> OpMyFixedLengthEnumListAsync(
                List<MyFixedLengthEnum> p1,
                List<MyFixedLengthEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyFixedLengthEnum> R1, IEnumerable<MyFixedLengthEnum> R2)> OpMyFixedLengthEnumCustomSeqAsync(
                CustomSequence<MyFixedLengthEnum> p1,
                CustomSequence<MyFixedLengthEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyUncheckedEnum> R1, IEnumerable<MyUncheckedEnum> R2)> OpMyUncheckedEnumListAsync(
                List<MyUncheckedEnum> p1,
                List<MyUncheckedEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyUncheckedEnum> R1, IEnumerable<MyUncheckedEnum> R2)> OpMyUncheckedEnumCustomSeqAsync(
                CustomSequence<MyUncheckedEnum> p1,
                CustomSequence<MyUncheckedEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyCompactStruct> R1, IEnumerable<MyCompactStruct> R2)> OpMyCompactStructListAsync(
                List<MyCompactStruct> p1,
                List<MyCompactStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<MyCompactStruct> R1, IEnumerable<MyCompactStruct> R2)> OpMyCompactStructCustomSeqAsync(
                CustomSequence<MyCompactStruct> p1,
                CustomSequence<MyCompactStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<OperationsPrx> R1, IEnumerable<OperationsPrx> R2)> OpOperationsListAsync(
                List<OperationsPrx> p1,
                List<OperationsPrx> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<OperationsPrx> R1, IEnumerable<OperationsPrx> R2)> OpOperationsCustomSeqAsync(
                CustomSequence<OperationsPrx> p1,
                CustomSequence<OperationsPrx> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<AnotherCompactStruct> R1, IEnumerable<AnotherCompactStruct> R2)> OpAnotherCompactStructListAsync(
                List<AnotherCompactStruct> p1,
                List<AnotherCompactStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<AnotherCompactStruct> R1, IEnumerable<AnotherCompactStruct> R2)> OpAnotherCompactStructCustomSeqAsync(
                CustomSequence<AnotherCompactStruct> p1,
                CustomSequence<AnotherCompactStruct> p2,
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
