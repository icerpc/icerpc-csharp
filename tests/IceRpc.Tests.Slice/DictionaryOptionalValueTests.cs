// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("icerpc")]
    public sealed class DictionaryOptionalValueTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly DictionaryOptionalValueOperationsPrx _prx;

        public DictionaryOptionalValueTests(string protocol)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocol)
                .AddTransient<IDispatcher, DictionaryOperations>()
                .BuildServiceProvider();
            _prx = DictionaryOptionalValueOperationsPrx.FromConnection(
                _serviceProvider.GetRequiredService<Connection>());
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

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
                (p1, p2) => _prx.OpOptionalMyCompactStructDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(
                    i => new MyCompactStruct(i, i + 1),
                    i => i % 2 == 0 ? (MyCompactStruct?)new MyCompactStruct(i, i + 1) : null),
                Enumerable.Range(0, size).ToDictionary(
                    i => new MyCompactStruct(i, i + 1),
                    i => i % 2 == 0 ? (MyCompactStruct?)new MyCompactStruct(i, i + 1) : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalAnotherCompactStructDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(
                    i => $"key-{i}",
                    i => i % 2 == 0 ? (AnotherCompactStruct?)GetAnotherCompactStruct(i) : null),
                Enumerable.Range(0, size).ToDictionary(
                    i => $"key-{i}",
                    i => i % 2 == 0 ? (AnotherCompactStruct?)GetAnotherCompactStruct(i) : null));

            await TestDictAsync(
                (p1, p2) => _prx.OpOptionalOperationsDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(
                    i => $"key-{i}",
                    i => i % 2 == 0 ? (OperationsPrx?)GetOperationsPrx(i) : null),
                Enumerable.Range(0, size).ToDictionary(
                    i => $"key-{i}",
                    i => i % 2 == 0 ? (OperationsPrx?)GetOperationsPrx(i) : null));

            static T GetEnum<T>(Array values, int i) => (T)values.GetValue(i % values.Length)!;

            OperationsPrx GetOperationsPrx(int i) => OperationsPrx.Parse($"icerpc://host/foo-{i}");

            AnotherCompactStruct GetAnotherCompactStruct(int i)
            {
                return new AnotherCompactStruct($"hello-{i}",
                                         GetOperationsPrx(i),
                                         (MyEnum)myEnumValues.GetValue(i % myEnumValues.Length)!,
                                         new MyCompactStruct(i, i + 1));
            }
        }

        public class DictionaryOperations : Service, IDictionaryOptionalValueOperations
        {
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

            public ValueTask<(IEnumerable<KeyValuePair<MyCompactStruct, MyCompactStruct?>> R1, IEnumerable<KeyValuePair<MyCompactStruct, MyCompactStruct?>> R2)> OpOptionalMyCompactStructDictAsync(
                Dictionary<MyCompactStruct, MyCompactStruct?> p1,
                Dictionary<MyCompactStruct, MyCompactStruct?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<string, OperationsPrx?>> R1, IEnumerable<KeyValuePair<string, OperationsPrx?>> R2)> OpOptionalOperationsDictAsync(
                Dictionary<string, OperationsPrx?> p1,
                Dictionary<string, OperationsPrx?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<string, AnotherCompactStruct?>> R1, IEnumerable<KeyValuePair<string, AnotherCompactStruct?>> R2)> OpOptionalAnotherCompactStructDictAsync(
                Dictionary<string, AnotherCompactStruct?> p1,
                Dictionary<string, AnotherCompactStruct?> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));
        }

        static async Task TestDictAsync<Key, Value>(
                Func<Dictionary<Key, Value>, Dictionary<Key, Value>, Task<(Dictionary<Key, Value>, Dictionary<Key, Value>)>> invoker,
                Dictionary<Key, Value> p1,
                Dictionary<Key, Value> p2) where Key : notnull
        {
            (Dictionary<Key, Value> r1, Dictionary<Key, Value> r2) = await invoker(p1, p2);
            Assert.That(r1, Is.EqualTo(p1));
            Assert.That(r2, Is.EqualTo(p2));
        }
    }
}
