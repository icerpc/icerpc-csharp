// Copyright (c) ZeroC, Inc. All rights reserved.

using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice
{
    [Timeout(5000)]
    [Parallelizable(ParallelScope.All)]
    [TestFixture("ice")]
    [TestFixture("icerpc")]
    public sealed class CompactStructTests
    {
        private readonly CompactStructOperationsPrx _prx;
        private readonly ServiceProvider _serviceProvider;

        public CompactStructTests(string protocolCode)
        {
            _serviceProvider = new IntegrationTestServiceCollection()
                .UseProtocol(protocolCode)
                .AddTransient<IDispatcher, CompactStructOperations>()
                .BuildServiceProvider();

            _prx = CompactStructOperationsPrx.FromConnection(_serviceProvider.GetRequiredService<Connection>());
        }

        [OneTimeTearDown]
        public ValueTask DisposeAsync() => _serviceProvider.DisposeAsync();

        [Test]
        public async Task CompactStruct_OperationsAsync()
        {
            // TODO Parse below should not use a connection with a different endpoint
            await TestAsync((p1, p2) => _prx.OpMyCompactStructAsync(p1, p2),
                            new MyCompactStruct(1, 2),
                            new MyCompactStruct(3, 4));
            await TestAsync((p1, p2) => _prx.OpAnotherCompactStructAsync(p1, p2),
                            new AnotherCompactStruct("hello",
                                              OperationsPrx.Parse("icerpc://foo/bar"),
                                              MyEnum.enum1,
                                              new MyCompactStruct(1, 2)),
                            new AnotherCompactStruct("world",
                                              OperationsPrx.Parse("icerpc://foo/bar"),
                                              MyEnum.enum2,
                                              new MyCompactStruct(3, 4)));
        }

        [Test]
        public async Task CompactStruct_CustomTypeAsync()
        {
            TimeSpan t1 = await _prx.OpMyTimeSpan1Async(TimeSpan.FromMilliseconds(100));
           Assert.That(TimeSpan.FromMilliseconds(100), Is.EqualTo(t1));

            (TimeSpan t2, TimeSpan t3) = await _prx.OpMyTimeSpan2Async(
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(200));
           Assert.That(TimeSpan.FromMilliseconds(100), Is.EqualTo(t2));
           Assert.That(TimeSpan.FromMilliseconds(200), Is.EqualTo(t3));

            MyStructWithCustomType s1 = await _prx.OpMyStructWithCustomTypeAsync(new MyStructWithCustomType(t1));
           Assert.That(TimeSpan.FromMilliseconds(100), Is.EqualTo(s1.Timespan));

            LinkedList<int> l1 = await _prx.OpMyList1Async(new LinkedList<int>(new int[] { 1, 2, 3 }));
           Assert.That(new LinkedList<int>(new int[] { 1, 2, 3 }), Is.EqualTo(l1));
        }

        static async Task TestAsync<T>(Func<T, T, Task<(T, T)>> invoker, T p1, T p2)
        {
            (T r1, T r2) = await invoker(p1, p2);
           Assert.That(r1, Is.EqualTo(p1));
           Assert.That(r2, Is.EqualTo(p2));
        }

        public class CompactStructOperations : Service, ICompactStructOperations
        {
            public ValueTask<(MyCompactStruct R1, MyCompactStruct R2)> OpMyCompactStructAsync(
                MyCompactStruct p1,
                MyCompactStruct p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<(AnotherCompactStruct R1, AnotherCompactStruct R2)> OpAnotherCompactStructAsync(
                AnotherCompactStruct p1,
                AnotherCompactStruct p2,
                Dispatch dispatch,
                CancellationToken cancel) => new((p1, p2));

            public ValueTask<TimeSpan> OpMyTimeSpan1Async(
                TimeSpan p1,
                Dispatch dispatch,
                CancellationToken cancel = default) => new(p1);

            public ValueTask<(TimeSpan R1, TimeSpan R2)> OpMyTimeSpan2Async(
                TimeSpan p1,
                TimeSpan p2,
                Dispatch dispatch,
                CancellationToken cancel = default) => new((p1, p2));

            public ValueTask<MyStructWithCustomType> OpMyStructWithCustomTypeAsync(
                MyStructWithCustomType p1,
                Dispatch dispatch,
                CancellationToken cancel = default) => new(p1);

            public ValueTask<LinkedList<int>> OpMyList1Async(
                LinkedList<int> p1,
                Dispatch dispatch,
                CancellationToken cancel = default) => new(p1);

            public ValueTask<(LinkedList<int> R1, LinkedList<int> R2)> OpMyList2Async(
                LinkedList<int> p1,
                LinkedList<int> p2,
                Dispatch dispatch,
                CancellationToken cancel = default) => new((p1, p2));
        }
    }
    public partial record struct MyCompactStruct
    {
        // Test overrides

        public readonly bool Equals(MyCompactStruct other) => I == other.I;

        public override readonly int GetHashCode() => I.GetHashCode();

        public override readonly string ToString() => $"{I + J}";
    }

    public static class SliceEncoderMyTimeSpanExtensions
    {
        public static void EncodeMyTimeSpan(ref SliceEncoder encoder, TimeSpan value) =>
            encoder.EncodeVarLong(checked((long)value.TotalMilliseconds));
    }

    public static class SliceDecoderMyTimeSpanExtensions
    {

        public static TimeSpan DecodeMyTimeSpan(ref SliceDecoder decoder) =>
            TimeSpan.FromMilliseconds(decoder.DecodeVarLong());
    }

    public static class SliceEncoderMyListExtensions
    {
        public static void EncodeMyList(ref SliceEncoder encoder, LinkedList<int> value) =>
            encoder.EncodeSequence(value.ToArray());
    }

    public static class SliceDecoderMyListExtensions
    {
        public static LinkedList<int> DecodeMyList(ref SliceDecoder decoder) =>
            new(decoder.DecodeSequence<int>(null).Reverse());
    }
}
