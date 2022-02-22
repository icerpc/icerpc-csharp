// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(30000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("ice")]
    [TestFixture("icerpc")]
    public sealed class DictionaryTests
    {
        private readonly ServiceProvider _serviceProvider;
        private readonly DictionaryOperationsPrx _prx;

        public DictionaryTests(string protocol)
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
            await TestCustomDictAsync(
               (p1, p2) => _prx.OpByteCustomDictAsync(p1, p2),
               new CustomDictionary<byte, byte>(
                   Enumerable.Range(0, size).Select(i => (byte)i).ToDictionary(key => key, value => value)),
               new CustomDictionary<byte, byte>(
                   Enumerable.Range(0, size).Select(i => (byte)i).ToDictionary(key => key, value => value)));

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpBoolCustomDictAsync(p1, p2),
                new CustomDictionary<bool, bool>(
                    Enumerable.Range(0, 2).Select(i => i % 2 == 0).ToDictionary(key => key, value => value)),
                new CustomDictionary<bool, bool>(
                    Enumerable.Range(0, 2).Select(i => i % 2 == 0).ToDictionary(key => key, value => value)));

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpShortCustomDictAsync(p1, p2),
                new CustomDictionary<short, short>(
                    Enumerable.Range(0, size).Select(i => (short)i).ToDictionary(key => key, value => value)),
                new CustomDictionary<short, short>(
                    Enumerable.Range(0, size).Select(i => (short)i).ToDictionary(key => key, value => value)));

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpUShortCustomDictAsync(p1, p2),
                new CustomDictionary<ushort, ushort>(
                    Enumerable.Range(0, size).Select(i => (ushort)i).ToDictionary(key => key, value => value)),
                new CustomDictionary<ushort, ushort>(
                    Enumerable.Range(0, size).Select(i => (ushort)i).ToDictionary(key => key, value => value)));

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpIntCustomDictAsync(p1, p2),
                new CustomDictionary<int, int>(Enumerable.Range(0, size).ToDictionary(key => key, value => value)),
                new CustomDictionary<int, int>(Enumerable.Range(0, size).ToDictionary(key => key, value => value)));

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpVarIntCustomDictAsync(p1, p2),
                new CustomDictionary<int, int>(Enumerable.Range(0, size).ToDictionary(key => key, value => value)),
                new CustomDictionary<int, int>(Enumerable.Range(0, size).ToDictionary(key => key, value => value)));

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpUIntCustomDictAsync(p1, p2),
                new CustomDictionary<uint, uint>(
                    Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value)),
                new CustomDictionary<uint, uint>(
                    Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value)));

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpVarUIntCustomDictAsync(p1, p2),
                new CustomDictionary<uint, uint>(
                    Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value)),
                new CustomDictionary<uint, uint>(
                    Enumerable.Range(0, size).Select(i => (uint)i).ToDictionary(key => key, value => value)));

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpLongCustomDictAsync(p1, p2),
                new CustomDictionary<long, long>(
                    Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value)),
                new CustomDictionary<long, long>(
                    Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value)));

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpVarLongCustomDictAsync(p1, p2),
                new CustomDictionary<long, long>(
                    Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value)),
                new CustomDictionary<long, long>(
                    Enumerable.Range(0, size).Select(i => (long)i).ToDictionary(key => key, value => value)));

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpULongCustomDictAsync(p1, p2),
                new CustomDictionary<ulong, ulong>(
                    Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value)),
                new CustomDictionary<ulong, ulong>(
                    Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value)));

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpVarULongCustomDictAsync(p1, p2),
                new CustomDictionary<ulong, ulong>(
                    Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value)),
                new CustomDictionary<ulong, ulong>(
                    Enumerable.Range(0, size).Select(i => (ulong)i).ToDictionary(key => key, value => value)));

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpStringCustomDictAsync(p1, p2),
                new CustomDictionary<string, string>(
                    Enumerable.Range(0, size).Select(i => $"hello-{i}").ToDictionary(key => key, value => value)),
                new CustomDictionary<string, string>(
                    Enumerable.Range(0, size).Select(i => $"hello-{i}").ToDictionary(key => key, value => value)));
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
                (p1, p2) => _prx.OpMyCompactStructDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(
                    i => new MyCompactStruct(i, i + 1),
                    i => new MyCompactStruct(i, i + 1)),
                Enumerable.Range(0, size).ToDictionary(
                    i => new MyCompactStruct(i, i + 1),
                    i => new MyCompactStruct(i, i + 1)));

            await TestDictAsync(
                (p1, p2) => _prx.OpAnotherCompactStructDictAsync(p1, p2),
                Enumerable.Range(0, size).ToDictionary(i => $"key-{i}", i => GetAnotherCompactStruct(i)),
                Enumerable.Range(0, size).ToDictionary(i => $"key-{i}", i => GetAnotherCompactStruct(i)));

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

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpMyFixedLengthEnumCustomDictAsync(p1, p2),
                new CustomDictionary<MyFixedLengthEnum, MyFixedLengthEnum>(
                    Enumerable.Range(0, myFixedLengthEnumValues.Length).ToDictionary(
                        i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i),
                        i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))),
                new CustomDictionary<MyFixedLengthEnum, MyFixedLengthEnum>(
                    Enumerable.Range(0, myFixedLengthEnumValues.Length).ToDictionary(
                        i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i),
                        i => GetEnum<MyFixedLengthEnum>(myFixedLengthEnumValues, i))));

            await TestCustomDictAsync(
                (p1, p2) => _prx.OpMyUncheckedEnumCustomDictAsync(p1, p2),
                new CustomDictionary<MyUncheckedEnum, MyUncheckedEnum>(
                    Enumerable.Range(0, size).ToDictionary(i => (MyUncheckedEnum)i, i => (MyUncheckedEnum)i)),
                new CustomDictionary<MyUncheckedEnum, MyUncheckedEnum>(
                    Enumerable.Range(0, size).ToDictionary(i => (MyUncheckedEnum)i, i => (MyUncheckedEnum)i)));

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

            public ValueTask<(IEnumerable<KeyValuePair<MyCompactStruct, MyCompactStruct>> R1, IEnumerable<KeyValuePair<MyCompactStruct, MyCompactStruct>> R2)> OpMyCompactStructDictAsync(
                Dictionary<MyCompactStruct, MyCompactStruct> p1,
                Dictionary<MyCompactStruct, MyCompactStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<string, OperationsPrx>> R1, IEnumerable<KeyValuePair<string, OperationsPrx>> R2)> OpOperationsDictAsync(
                Dictionary<string, OperationsPrx> p1,
                Dictionary<string, OperationsPrx> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<string, AnotherCompactStruct>> R1, IEnumerable<KeyValuePair<string, AnotherCompactStruct>> R2)> OpAnotherCompactStructDictAsync(
                Dictionary<string, AnotherCompactStruct> p1,
                Dictionary<string, AnotherCompactStruct> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<byte, byte>> R1, IEnumerable<KeyValuePair<byte, byte>> R2)> OpByteCustomDictAsync(
                CustomDictionary<byte, byte> p1,
                CustomDictionary<byte, byte> p2,
                Dispatch dispatch, CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<bool, bool>> R1, IEnumerable<KeyValuePair<bool, bool>> R2)> OpBoolCustomDictAsync(
                CustomDictionary<bool, bool> p1,
                CustomDictionary<bool, bool> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<short, short>> R1, IEnumerable<KeyValuePair<short, short>> R2)> OpShortCustomDictAsync(
                CustomDictionary<short, short> p1,
                CustomDictionary<short, short> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<ushort, ushort>> R1, IEnumerable<KeyValuePair<ushort, ushort>> R2)> OpUShortCustomDictAsync(
                CustomDictionary<ushort, ushort> p1,
                CustomDictionary<ushort, ushort> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<int, int>> R1, IEnumerable<KeyValuePair<int, int>> R2)> OpIntCustomDictAsync(
                CustomDictionary<int, int> p1,
                CustomDictionary<int, int> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<int, int>> R1, IEnumerable<KeyValuePair<int, int>> R2)> OpVarIntCustomDictAsync(
                CustomDictionary<int, int> p1,
                CustomDictionary<int, int> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));
            public ValueTask<(IEnumerable<KeyValuePair<uint, uint>> R1, IEnumerable<KeyValuePair<uint, uint>> R2)> OpUIntCustomDictAsync(
                CustomDictionary<uint, uint> p1,
                CustomDictionary<uint, uint> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<uint, uint>> R1, IEnumerable<KeyValuePair<uint, uint>> R2)> OpVarUIntCustomDictAsync(
                CustomDictionary<uint, uint> p1,
                CustomDictionary<uint, uint> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<long, long>> R1, IEnumerable<KeyValuePair<long, long>> R2)> OpLongCustomDictAsync(
                CustomDictionary<long, long> p1,
                CustomDictionary<long, long> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<long, long>> R1, IEnumerable<KeyValuePair<long, long>> R2)> OpVarLongCustomDictAsync(
                CustomDictionary<long, long> p1,
                CustomDictionary<long, long> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<ulong, ulong>> R1, IEnumerable<KeyValuePair<ulong, ulong>> R2)> OpULongCustomDictAsync(
                CustomDictionary<ulong, ulong> p1,
                CustomDictionary<ulong, ulong> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<ulong, ulong>> R1, IEnumerable<KeyValuePair<ulong, ulong>> R2)> OpVarULongCustomDictAsync(
                CustomDictionary<ulong, ulong> p1,
                CustomDictionary<ulong, ulong> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<string, string>> R1, IEnumerable<KeyValuePair<string, string>> R2)> OpStringCustomDictAsync(
                CustomDictionary<string, string> p1,
                CustomDictionary<string, string> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<MyEnum, MyEnum>> R1, IEnumerable<KeyValuePair<MyEnum, MyEnum>> R2)> OpMyEnumCustomDictAsync(
                CustomDictionary<MyEnum, MyEnum> p1,
                CustomDictionary<MyEnum, MyEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<MyFixedLengthEnum, MyFixedLengthEnum>> R1, IEnumerable<KeyValuePair<MyFixedLengthEnum, MyFixedLengthEnum>> R2)> OpMyFixedLengthEnumCustomDictAsync(
                CustomDictionary<MyFixedLengthEnum, MyFixedLengthEnum> p1,
                CustomDictionary<MyFixedLengthEnum, MyFixedLengthEnum> p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(IEnumerable<KeyValuePair<MyUncheckedEnum, MyUncheckedEnum>> R1, IEnumerable<KeyValuePair<MyUncheckedEnum, MyUncheckedEnum>> R2)> OpMyUncheckedEnumCustomDictAsync(
                CustomDictionary<MyUncheckedEnum, MyUncheckedEnum> p1,
                CustomDictionary<MyUncheckedEnum, MyUncheckedEnum> p2,
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

        static async Task TestCustomDictAsync<Key, Value>(
                Func<CustomDictionary<Key, Value>, CustomDictionary<Key, Value>, Task<(CustomDictionary<Key, Value>, CustomDictionary<Key, Value>)>> invoker,
                CustomDictionary<Key, Value> p1,
                CustomDictionary<Key, Value> p2) where Key : notnull
        {
            (CustomDictionary<Key, Value> r1, CustomDictionary<Key, Value> r2) = await invoker(p1, p2);
           Assert.That(r1, Is.EqualTo(p1));
           Assert.That(r2, Is.EqualTo(p2));
        }
    }
}
