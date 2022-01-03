// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture(ProtocolCode.Ice)]
    [TestFixture(ProtocolCode.IceRpc)]
    public sealed class DictionaryTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly DictionaryOperationsPrx _prx;

        public DictionaryTests(ProtocolCode protocol)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher, DictionaryOperations>()
                .BuildServiceProvider();
            _prx = DictionaryOperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task Dictionary_BuiltinTypesAsync()
        {
            int size = 100;
            await TestDictAsync(
                (p1, p2) => _prx.OpByteDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (byte)i).ToDictionary(key => key, value => value),
                Enumerable.Range(0, size).Select(i => (byte)i).ToDictionary(key => key, value => value));

            await TestDictAsync(
                (p1, p2) => _prx.OpBoolDictAsync(p1, p2),
                Enumerable.Range(0, 2).Select(i => i % 2 == 0).ToDictionary(key => key, value => value),
                Enumerable.Range(0, 2).Select(i => i % 2 == 0).ToDictionary(key => key, value => value));

            await TestDictAsync(
                (p1, p2) => _prx.OpShortDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (short)i).ToDictionary(key => key, value => value),
                Enumerable.Range(0, size).Select(i => (short)i).ToDictionary(key => key, value => value));

            await TestDictAsync(
                (p1, p2) => _prx.OpUShortDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (ushort)i).ToDictionary(key => key, value => value),
                Enumerable.Range(0, size).Select(i => (ushort)i).ToDictionary(key => key, value => value));

            await TestDictAsync(
                (p1, p2) => _prx.OpIntDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(key => key, value => value),
                Enumerable.Range(0, size).ToDictionary(key => key, value => value));

            await TestDictAsync(
                (p1, p2) => _prx.OpVarIntDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(key => key, value => value),
                Enumerable.Range(0, size).ToDictionary(key => key, value => value));

            await TestDictAsync(
                (p1, p2) => _prx.OpUIntDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value),
                Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value));

            await TestDictAsync(
                (p1, p2) => _prx.OpVarUIntDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value),
                Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value));

            await TestDictAsync(
                (p1, p2) => _prx.OpLongDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value),
                Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value));

            await TestDictAsync(
                (p1, p2) => _prx.OpVarLongDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value),
                Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value));

            await TestDictAsync(
                (p1, p2) => _prx.OpULongDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value),
                Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value));

            await TestDictAsync(
                (p1, p2) => _prx.OpVarULongDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value),
                Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value));

            await TestDictAsync(
                (p1, p2) => _prx.OpStringDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => $"hello-{i}").ToDictionary(key => key, value => value),
                Enumerable.Range(0, size).Select(i => $"hello-{i}").ToDictionary(key => key, value => value));

            // Sorted dictionaries
            await TestSortedDictAsync(
               (p1, p2) => _prx.OpByteSortedDictAsync(p1, p2),
               new SortedDictionary<byte, byte>(
                   Enumerable.Range(0, size).Select(i => (byte)i).ToDictionary(key => key, value => value)),
               new SortedDictionary<byte, byte>(
                   Enumerable.Range(0, size).Select(i => (byte)i).ToDictionary(key => key, value => value)));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpBoolSortedDictAsync(p1, p2),
                new SortedDictionary<bool, bool>(
                    Enumerable.Range(0, 2).Select(i => i % 2 == 0).ToDictionary(key => key, value => value)),
                new SortedDictionary<bool, bool>(
                    Enumerable.Range(0, 2).Select(i => i % 2 == 0).ToDictionary(key => key, value => value)));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpShortSortedDictAsync(p1, p2),
                new SortedDictionary<short, short>(
                    Enumerable.Range(0, size).Select(i => (short)i).ToDictionary(key => key, value => value)),
                new SortedDictionary<short, short>(
                    Enumerable.Range(0, size).Select(i => (short)i).ToDictionary(key => key, value => value)));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpUShortSortedDictAsync(p1, p2),
                new SortedDictionary<ushort, ushort>(
                    Enumerable.Range(0, size).Select(i => (ushort)i).ToDictionary(key => key, value => value)),
                new SortedDictionary<ushort, ushort>(
                    Enumerable.Range(0, size).Select(i => (ushort)i).ToDictionary(key => key, value => value)));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpIntSortedDictAsync(p1, p2),
                new SortedDictionary<int, int>(Enumerable.Range(0, size).ToDictionary(key => key, value => value)),
                new SortedDictionary<int, int>(Enumerable.Range(0, size).ToDictionary(key => key, value => value)));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpVarIntSortedDictAsync(p1, p2),
                new SortedDictionary<int, int>(Enumerable.Range(0, size).ToDictionary(key => key, value => value)),
                new SortedDictionary<int, int>(Enumerable.Range(0, size).ToDictionary(key => key, value => value)));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpUIntSortedDictAsync(p1, p2),
                new SortedDictionary<uint, uint>(
                    Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value)),
                new SortedDictionary<uint, uint>(
                    Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value)));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpVarUIntSortedDictAsync(p1, p2),
                new SortedDictionary<uint, uint>(
                    Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value)),
                new SortedDictionary<uint, uint>(
                    Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value)));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpLongSortedDictAsync(p1, p2),
                new SortedDictionary<long, long>(
                    Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value)),
                new SortedDictionary<long, long>(
                    Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value)));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpVarLongSortedDictAsync(p1, p2),
                new SortedDictionary<long, long>(
                    Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value)),
                new SortedDictionary<long, long>(
                    Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value)));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpULongSortedDictAsync(p1, p2),
                new SortedDictionary<ulong, ulong>(
                    Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value)),
                new SortedDictionary<ulong, ulong>(
                    Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value)));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpVarULongSortedDictAsync(p1, p2),
                new SortedDictionary<ulong, ulong>(
                    Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value)),
                new SortedDictionary<ulong, ulong>(
                    Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value)));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpStringSortedDictAsync(p1, p2),
                new SortedDictionary<string, string>(
                    Enumerable.Range(0, size).Select(i => $"hello-{i}").ToDictionary(key => key, value => value)),
                new SortedDictionary<string, string>(
                    Enumerable.Range(0, size).Select(i => $"hello-{i}").ToDictionary(key => key, value => value)));
        }

        [Test]
        public async Task Dictionary_OptionalBuiltinTypesAsync()
        {
            int size = 100;
            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalByteDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (byte)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (byte?)value : null),
                Enumerable.Range(0, size).Select(i => (byte)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (byte?)value : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalBoolDictAsync(p1, p2),
                Enumerable.Range(0, 2).Select(i => i % 2 == 0).ToDictionary(
                    key => key,
                    value => value ? (bool?)true : null),
                Enumerable.Range(0, 2).Select(i => i % 2 == 0).ToDictionary(
                    key => key,
                    value => value ? (bool?)true : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalShortDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (short)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (short?)value : null),
                Enumerable.Range(0, size).Select(i => (short)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (short?)value : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalUShortDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (ushort)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (ushort?)value : null),
                Enumerable.Range(0, size).Select(i => (ushort)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (ushort?)value : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalIntDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (int?)value : null),
                Enumerable.Range(0, size).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (int?)value : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalVarIntDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (int?)value : null),
                Enumerable.Range(0, size).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (int?)value : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalUIntDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (uint?)value : null),
                Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (uint?)value : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalVarUIntDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (uint?)value : null),
                Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (uint?)value : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalLongDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (long?)value : null),
                Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (long?)value : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalVarLongDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (long?)value : null),
                Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (long?)value : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalULongDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (ulong?)value : null),
                Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (ulong?)value : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalVarULongDictAsync(p1, p2),
                Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (ulong?)value : null),
                Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(
                    key => key,
                    value => value % 2 == 0 ? (ulong?)value : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalStringDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(
                    key => $"hello-{key}",
                    value => value % 2 == 0 ? (string?)$"hell-{value}" : null),
                Enumerable.Range(0, size).ToDictionary(
                    key => $"hello-{key}",
                    value => value % 2 == 0 ? (string?)$"hell-{value}" : null));
        }

        [Test]
        public async Task Dictionary_ConstructedTypesAsync()
        {
            int size = 100;
            Array myEnumValues = Enum.GetValues(typeof(MyEnum));
            await TestDictAsync(
                (p1, p2) => _prx.OpMyEnumDictAsync(p1, p2),
                Enumerable.Range(0, myEnumValues.Length).ToDictionary(
                    i => GetEnum<MyEnum>(myEnumValues, i),
                    i => GetEnum<MyEnum>(myEnumValues, i)),
                Enumerable.Range(0, myEnumValues.Length).ToDictionary(
                    i => GetEnum<MyEnum>(myEnumValues, i),
                    i => GetEnum<MyEnum>(myEnumValues, i)));

            Array myFixedLengthEnumValues = Enum.GetValues(typeof(MyFixedLengthEnum));
            await TestDictAsync(
                (p1, p2) => _prx.OpMyFixedLengthEnumDictAsync(p1, p2),
                Enumerable.Range(0, myFixedLengthEnumValues.Length).ToDictionary(
                    i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i),
                    i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i)),
                Enumerable.Range(0, myFixedLengthEnumValues.Length).ToDictionary(
                    i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i),
                    i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i)));

            await TestDictAsync(
                (p1, p2) => _prx.OpMyUncheckedEnumDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(i => (MyUncheckedEnum)i, i => (MyUncheckedEnum)i),
                Enumerable.Range(0, size).ToDictionary(i => (MyUncheckedEnum)i, i => (MyUncheckedEnum)i));

            await TestDictAsync(
                (p1, p2) => _prx.OpMyStructDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(i => new MyStruct(i, i + 1), i => new MyStruct(i, i + 1)),
                Enumerable.Range(0, size).ToDictionary(i => new MyStruct(i, i + 1), i => new MyStruct(i, i + 1)));

            await TestDictAsync(
                (p1, p2) => _prx.OpAnotherStructDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(i => $"key-{i}", i => GetAnotherStruct(i)),
                Enumerable.Range(0, size).ToDictionary(i => $"key-{i}", i => GetAnotherStruct(i)));

            await TestDictAsync(
                (p1, p2) => _prx.OpOperationsDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(i => $"key-{i}", i => GetOperationsPrx(i)),
                Enumerable.Range(0, size).ToDictionary(i => $"key-{i}", i => GetOperationsPrx(i)));

            // repeat with sorted dictionaries
            await TestDictAsync(
                (p1, p2) => _prx.OpMyEnumDictAsync(p1, p2),
                Enumerable.Range(0, myEnumValues.Length).ToDictionary(
                    i => GetEnum<MyEnum>(myEnumValues, i),
                    i => GetEnum<MyEnum>(myEnumValues, i)),
                Enumerable.Range(0, myEnumValues.Length).ToDictionary(
                    i => GetEnum<MyEnum>(myEnumValues, i),
                    i => GetEnum<MyEnum>(myEnumValues, i)));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpMyFixedLengthEnumSortedDictAsync(p1, p2),
                new SortedDictionary<MyFixedLengthEnum, MyFixedLengthEnum>(
                    Enumerable.Range(0, myFixedLengthEnumValues.Length).ToDictionary(
                        i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i),
                        i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))),
                new SortedDictionary<MyFixedLengthEnum, MyFixedLengthEnum>(
                    Enumerable.Range(0, myFixedLengthEnumValues.Length).ToDictionary(
                        i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i),
                        i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))));

            await TestSortedDictAsync(
                (p1, p2) => _prx.OpMyUncheckedEnumSortedDictAsync(p1, p2),
                new SortedDictionary<MyUncheckedEnum, MyUncheckedEnum>(
                    Enumerable.Range(0, size).ToDictionary(i => (MyUncheckedEnum)i, i => (MyUncheckedEnum)i)),
                new SortedDictionary<MyUncheckedEnum, MyUncheckedEnum>(
                    Enumerable.Range(0, size).ToDictionary(i => (MyUncheckedEnum)i, i => (MyUncheckedEnum)i)));

            static T GetEnum<T>(Array values, int i) => (T)values.GetValue(i % values.Length)!;

            OperationsPrx GetOperationsPrx(int i) => OperationsPrx.Parse($"icerpc+tcp://host/foo-{i}");

            AnotherStruct GetAnotherStruct(int i)
            {
                return new AnotherStruct($"hello-{i}",
                                         GetOperationsPrx(i),
                                         (MyEnum)myEnumValues.GetValue(i % myEnumValues.Length)!,
                                         new MyStruct(i, i + 1));
            }
        }

        [Test]
        public async Task Dictionary_OptionalConstructedTypesAsync()
        {
            int size = 100;
            Array myEnumValues = Enum.GetValues(typeof(MyEnum));
            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalMyEnumDictAsync(p1, p2),
                Enumerable.Range(0, myEnumValues.Length).ToDictionary(
                    i => GetEnum<MyEnum>(myEnumValues, i),
                    i => i % 2 == 0 ? (MyEnum?)GetEnum<MyEnum>(myEnumValues, i) : null),
                Enumerable.Range(0, myEnumValues.Length).ToDictionary(
                    i => GetEnum<MyEnum>(myEnumValues, i),
                    i => i % 2 == 0 ? (MyEnum?)GetEnum<MyEnum>(myEnumValues, i) : null));

            Array myFixedLengthEnumValues = Enum.GetValues(typeof(MyFixedLengthEnum));
            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalMyFixedLengthEnumDictAsync(p1, p2),
                Enumerable.Range(0, myFixedLengthEnumValues.Length).ToDictionary(
                    i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i),
                    i => i % 2 == 0 ? (MyFixedLengthEnum?)GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i) : null),
                Enumerable.Range(0, myFixedLengthEnumValues.Length).ToDictionary(
                    i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i),
                    i => i % 2 == 0 ? (MyFixedLengthEnum?)GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i) : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalMyUncheckedEnumDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(
                    i => (MyUncheckedEnum)i,
                    i => i % 2 == 0 ? (MyUncheckedEnum?)i : null),
                Enumerable.Range(0, size).ToDictionary(
                    i => (MyUncheckedEnum)i,
                    i => i % 2 == 0 ? (MyUncheckedEnum?)i : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalMyStructDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(
                    i => new MyStruct(i, i + 1),
                    i => i % 2 == 0 ? (MyStruct?)new MyStruct(i, i + 1) : null),
                Enumerable.Range(0, size).ToDictionary(
                    i => new MyStruct(i, i + 1),
                    i => i % 2 == 0 ? (MyStruct?)new MyStruct(i, i + 1) : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalAnotherStructDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(
                    i => $"key-{i}",
                    i => i % 2 == 0 ? (AnotherStruct?)GetAnotherStruct(i) : null),
                Enumerable.Range(0, size).ToDictionary(
                    i => $"key-{i}",
                    i => i % 2 == 0 ? (AnotherStruct?)GetAnotherStruct(i) : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalOperationsDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(
                    i => $"key-{i}",
                    i => i % 2 == 0 ? (OperationsPrx?)GetOperationsPrx(i) : null),
                Enumerable.Range(0, size).ToDictionary(
                    i => $"key-{i}",
                    i => i % 2 == 0 ? (OperationsPrx?)GetOperationsPrx(i) : null));

            static T GetEnum<T>(Array values, int i) => (T)values.GetValue(i % values.Length)!;

            OperationsPrx GetOperationsPrx(int i) => OperationsPrx.Parse($"icerpc+tcp://host/foo-{i}");

            AnotherStruct GetAnotherStruct(int i)
            {
                return new AnotherStruct($"hello-{i}",
                                         GetOperationsPrx(i),
                                         (MyEnum)myEnumValues.GetValue(i % myEnumValues.Length)!,
                                         new MyStruct(i, i + 1));
            }
        }

        public class DictionaryOperations : Service, IDictionaryOperations
        {
            // Builtin types dictionaries
            public ValueTask<(IEnumerable<KeyValuePair<byte, byte>> R1, IEnumerable<KeyValuePair<byte, byte>> R2)> OpByteDictAsync(
                Dictionary<byte, byte> p1,
                Dictionary<byte, byte> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<bool, bool>> R1, IEnumerable<KeyValuePair<bool, bool>> R2)> OpBoolDictAsync(
                Dictionary<bool, bool> p1,
                Dictionary<bool, bool> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<short, short>> R1, IEnumerable<KeyValuePair<short, short>> R2)> OpShortDictAsync(
                Dictionary<short, short> p1,
                Dictionary<short, short> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<ushort, ushort>> R1, IEnumerable<KeyValuePair<ushort, ushort>> R2)> OpUShortDictAsync(
                Dictionary<ushort, ushort> p1,
                Dictionary<ushort, ushort> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<int, int>> R1, IEnumerable<KeyValuePair<int, int>> R2)> OpIntDictAsync(
                Dictionary<int, int> p1,
                Dictionary<int, int> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<int, int>> R1, IEnumerable<KeyValuePair<int, int>> R2)> OpVarIntDictAsync(
                Dictionary<int, int> p1,
                Dictionary<int, int> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<uint, uint>> R1, IEnumerable<KeyValuePair<uint, uint>> R2)> OpUIntDictAsync(
                Dictionary<uint, uint> p1,
                Dictionary<uint, uint> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<uint, uint>> R1, IEnumerable<KeyValuePair<uint, uint>> R2)> OpVarUIntDictAsync(
                Dictionary<uint, uint> p1,
                Dictionary<uint, uint> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<long, long>> R1, IEnumerable<KeyValuePair<long, long>> R2)> OpLongDictAsync(
                Dictionary<long, long> p1,
                Dictionary<long, long> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<long, long>> R1, IEnumerable<KeyValuePair<long, long>> R2)> OpVarLongDictAsync(
                Dictionary<long, long> p1,
                Dictionary<long, long> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<ulong, ulong>> R1, IEnumerable<KeyValuePair<ulong, ulong>> R2)> OpULongDictAsync(
                Dictionary<ulong, ulong> p1,
                Dictionary<ulong, ulong> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<ulong, ulong>> R1, IEnumerable<KeyValuePair<ulong, ulong>> R2)> OpVarULongDictAsync(
                Dictionary<ulong, ulong> p1,
                Dictionary<ulong, ulong> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<string, string>> R1, IEnumerable<KeyValuePair<string, string>> R2)> OpStringDictAsync(
                Dictionary<string, string> p1,
                Dictionary<string, string> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            // Optional builtin types dictionaries
            public ValueTask<(IEnumerable<KeyValuePair<byte, byte?>> R1, IEnumerable<KeyValuePair<byte, byte?>> R2)> OpOptionalByteDictAsync(
                Dictionary<byte, byte?> p1,
                Dictionary<byte, byte?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<bool, bool?>> R1, IEnumerable<KeyValuePair<bool, bool?>> R2)> OpOptionalBoolDictAsync(
                Dictionary<bool, bool?> p1,
                Dictionary<bool, bool?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<short, short?>> R1, IEnumerable<KeyValuePair<short, short?>> R2)> OpOptionalShortDictAsync(
                Dictionary<short, short?> p1,
                Dictionary<short, short?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<ushort, ushort?>> R1, IEnumerable<KeyValuePair<ushort, ushort?>> R2)> OpOptionalUShortDictAsync(
                Dictionary<ushort, ushort?> p1,
                Dictionary<ushort, ushort?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<int, int?>> R1, IEnumerable<KeyValuePair<int, int?>> R2)> OpOptionalIntDictAsync(
                Dictionary<int, int?> p1,
                Dictionary<int, int?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<int, int?>> R1, IEnumerable<KeyValuePair<int, int?>> R2)> OpOptionalVarIntDictAsync(
                Dictionary<int, int?> p1,
                Dictionary<int, int?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<uint, uint?>> R1, IEnumerable<KeyValuePair<uint, uint?>> R2)> OpOptionalUIntDictAsync(
                Dictionary<uint, uint?> p1,
                Dictionary<uint, uint?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<uint, uint?>> R1, IEnumerable<KeyValuePair<uint, uint?>> R2)> OpOptionalVarUIntDictAsync(
                Dictionary<uint, uint?> p1,
                Dictionary<uint, uint?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<long, long?>> R1, IEnumerable<KeyValuePair<long, long?>> R2)> OpOptionalLongDictAsync(
                Dictionary<long, long?> p1,
                Dictionary<long, long?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<long, long?>> R1, IEnumerable<KeyValuePair<long, long?>> R2)> OpOptionalVarLongDictAsync(
                Dictionary<long, long?> p1,
                Dictionary<long, long?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<ulong, ulong?>> R1, IEnumerable<KeyValuePair<ulong, ulong?>> R2)> OpOptionalULongDictAsync(
                Dictionary<ulong, ulong?> p1,
                Dictionary<ulong, ulong?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<ulong, ulong?>> R1, IEnumerable<KeyValuePair<ulong, ulong?>> R2)> OpOptionalVarULongDictAsync(
                Dictionary<ulong, ulong?> p1,
                Dictionary<ulong, ulong?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<string, string?>> R1, IEnumerable<KeyValuePair<string, string?>> R2)> OpOptionalStringDictAsync(
                Dictionary<string, string?> p1,
                Dictionary<string, string?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            // Dictionaries with constructed types
            public ValueTask<(IEnumerable<KeyValuePair<MyEnum, MyEnum>> R1, IEnumerable<KeyValuePair<MyEnum, MyEnum>> R2)> OpMyEnumDictAsync(
                Dictionary<MyEnum, MyEnum> p1,
                Dictionary<MyEnum, MyEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<MyFixedLengthEnum, MyFixedLengthEnum>> R1, IEnumerable<KeyValuePair<MyFixedLengthEnum, MyFixedLengthEnum>> R2)> OpMyFixedLengthEnumDictAsync(
                Dictionary<MyFixedLengthEnum, MyFixedLengthEnum> p1,
                Dictionary<MyFixedLengthEnum, MyFixedLengthEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<MyUncheckedEnum, MyUncheckedEnum>> R1, IEnumerable<KeyValuePair<MyUncheckedEnum, MyUncheckedEnum>> R2)> OpMyUncheckedEnumDictAsync(
                Dictionary<MyUncheckedEnum, MyUncheckedEnum> p1,
                Dictionary<MyUncheckedEnum, MyUncheckedEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<MyStruct, MyStruct>> R1, IEnumerable<KeyValuePair<MyStruct, MyStruct>> R2)> OpMyStructDictAsync(
                Dictionary<MyStruct, MyStruct> p1,
                Dictionary<MyStruct, MyStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<string, OperationsPrx>> R1, IEnumerable<KeyValuePair<string, OperationsPrx>> R2)> OpOperationsDictAsync(
                Dictionary<string, OperationsPrx> p1,
                Dictionary<string, OperationsPrx> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<string, AnotherStruct>> R1, IEnumerable<KeyValuePair<string, AnotherStruct>> R2)> OpAnotherStructDictAsync(
                Dictionary<string, AnotherStruct> p1,
                Dictionary<string, AnotherStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<byte, byte>> R1, IEnumerable<KeyValuePair<byte, byte>> R2)> OpByteSortedDictAsync(
                SortedDictionary<byte, byte> p1,
                SortedDictionary<byte, byte> p2,
                Dispatch dispatch, CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<bool, bool>> R1, IEnumerable<KeyValuePair<bool, bool>> R2)> OpBoolSortedDictAsync(
                SortedDictionary<bool, bool> p1,
                SortedDictionary<bool, bool> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<short, short>> R1, IEnumerable<KeyValuePair<short, short>> R2)> OpShortSortedDictAsync(
                SortedDictionary<short, short> p1,
                SortedDictionary<short, short> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<ushort, ushort>> R1, IEnumerable<KeyValuePair<ushort, ushort>> R2)> OpUShortSortedDictAsync(
                SortedDictionary<ushort, ushort> p1,
                SortedDictionary<ushort, ushort> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<int, int>> R1, IEnumerable<KeyValuePair<int, int>> R2)> OpIntSortedDictAsync(
                SortedDictionary<int, int> p1,
                SortedDictionary<int, int> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<int, int?>> R1, IEnumerable<KeyValuePair<int, int?>> R2)> OpOptionalIntSortedDictAsync(
                SortedDictionary<int, int?> p1,
                SortedDictionary<int, int?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<int, int>> R1, IEnumerable<KeyValuePair<int, int>> R2)> OpVarIntSortedDictAsync(
                SortedDictionary<int, int> p1,
                SortedDictionary<int, int> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));
            public ValueTask<(IEnumerable<KeyValuePair<uint, uint>> R1, IEnumerable<KeyValuePair<uint, uint>> R2)> OpUIntSortedDictAsync(
                SortedDictionary<uint, uint> p1,
                SortedDictionary<uint, uint> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<uint, uint>> R1, IEnumerable<KeyValuePair<uint, uint>> R2)> OpVarUIntSortedDictAsync(
                SortedDictionary<uint, uint> p1,
                SortedDictionary<uint, uint> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<long, long>> R1, IEnumerable<KeyValuePair<long, long>> R2)> OpLongSortedDictAsync(
                SortedDictionary<long, long> p1,
                SortedDictionary<long, long> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<long, long>> R1, IEnumerable<KeyValuePair<long, long>> R2)> OpVarLongSortedDictAsync(
                SortedDictionary<long, long> p1,
                SortedDictionary<long, long> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<ulong, ulong>> R1, IEnumerable<KeyValuePair<ulong, ulong>> R2)> OpULongSortedDictAsync(
                SortedDictionary<ulong, ulong> p1,
                SortedDictionary<ulong, ulong> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<ulong, ulong>> R1, IEnumerable<KeyValuePair<ulong, ulong>> R2)> OpVarULongSortedDictAsync(
                SortedDictionary<ulong, ulong> p1,
                SortedDictionary<ulong, ulong> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<string, string>> R1, IEnumerable<KeyValuePair<string, string>> R2)> OpStringSortedDictAsync(
                SortedDictionary<string, string> p1,
                SortedDictionary<string, string> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<string, string?>> R1, IEnumerable<KeyValuePair<string, string?>> R2)> OpOptionalStringSortedDictAsync(
                SortedDictionary<string, string?> p1,
                SortedDictionary<string, string?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<MyEnum, MyEnum>> R1, IEnumerable<KeyValuePair<MyEnum, MyEnum>> R2)> OpMyEnumSortedDictAsync(
                SortedDictionary<MyEnum, MyEnum> p1,
                SortedDictionary<MyEnum, MyEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<MyFixedLengthEnum, MyFixedLengthEnum>> R1, IEnumerable<KeyValuePair<MyFixedLengthEnum, MyFixedLengthEnum>> R2)> OpMyFixedLengthEnumSortedDictAsync(
                SortedDictionary<MyFixedLengthEnum, MyFixedLengthEnum> p1,
                SortedDictionary<MyFixedLengthEnum, MyFixedLengthEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<MyUncheckedEnum, MyUncheckedEnum>> R1, IEnumerable<KeyValuePair<MyUncheckedEnum, MyUncheckedEnum>> R2)> OpMyUncheckedEnumSortedDictAsync(
                SortedDictionary<MyUncheckedEnum, MyUncheckedEnum> p1,
                SortedDictionary<MyUncheckedEnum, MyUncheckedEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            // Dictionaries with optional constructed types
            public ValueTask<(IEnumerable<KeyValuePair<MyEnum, MyEnum?>> R1, IEnumerable<KeyValuePair<MyEnum, MyEnum?>> R2)> OpOptionalMyEnumDictAsync(
                Dictionary<MyEnum, MyEnum?> p1,
                Dictionary<MyEnum, MyEnum?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<MyFixedLengthEnum, MyFixedLengthEnum?>> R1, IEnumerable<KeyValuePair<MyFixedLengthEnum, MyFixedLengthEnum?>> R2)> OpOptionalMyFixedLengthEnumDictAsync(
                Dictionary<MyFixedLengthEnum, MyFixedLengthEnum?> p1,
                Dictionary<MyFixedLengthEnum, MyFixedLengthEnum?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<MyUncheckedEnum, MyUncheckedEnum?>> R1, IEnumerable<KeyValuePair<MyUncheckedEnum, MyUncheckedEnum?>> R2)> OpOptionalMyUncheckedEnumDictAsync(
                Dictionary<MyUncheckedEnum, MyUncheckedEnum?> p1,
                Dictionary<MyUncheckedEnum, MyUncheckedEnum?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<MyStruct, MyStruct?>> R1, IEnumerable<KeyValuePair<MyStruct, MyStruct?>> R2)> OpOptionalMyStructDictAsync(
                Dictionary<MyStruct, MyStruct?> p1,
                Dictionary<MyStruct, MyStruct?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<string, OperationsPrx?>> R1, IEnumerable<KeyValuePair<string, OperationsPrx?>> R2)> OpOptionalOperationsDictAsync(
                Dictionary<string, OperationsPrx?> p1,
                Dictionary<string, OperationsPrx?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<string, AnotherStruct?>> R1, IEnumerable<KeyValuePair<string, AnotherStruct?>> R2)> OpOptionalAnotherStructDictAsync(
                Dictionary<string, AnotherStruct?> p1,
                Dictionary<string, AnotherStruct?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));
        }

        static async Task TestDictAsync<Key, Value>(
                Func<Dictionary<Key, Value>, Dictionary<Key, Value>, Task<(Dictionary<Key, Value>, Dictionary<Key, Value>)>> invoker,
                Dictionary<Key, Value> p1,
                Dictionary<Key, Value> p2) where Key : notnull
        {
            (Dictionary<Key, Value> r1, Dictionary<Key, Value> r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(p1, r1);
            CollectionAssert.AreEqual(p2, r2);
        }

        static async Task TestSortedDictAsync<Key, Value>(
                Func<SortedDictionary<Key, Value>, SortedDictionary<Key, Value>, Task<(SortedDictionary<Key, Value>, SortedDictionary<Key, Value>)>> invoker,
                SortedDictionary<Key, Value> p1,
                SortedDictionary<Key, Value> p2) where Key : notnull
        {
            (SortedDictionary<Key, Value> r1, SortedDictionary<Key, Value> r2) = await invoker(p1, p2);
            CollectionAssert.AreEqual(p1, r1);
            CollectionAssert.AreEqual(p2, r2);
        }
    }
}
