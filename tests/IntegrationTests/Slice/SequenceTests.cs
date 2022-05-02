// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

public class SequenceTests
{
    class SequenceOperations : Service, ISequenceOperations
    {
        public ValueTask<ReadOnlyMemory<bool>> Op1Async(
            bool[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<ReadOnlyMemory<sbyte>> Op2Async(
            sbyte[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<ReadOnlyMemory<byte>> Op3Async(
            byte[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<ReadOnlyMemory<short>> Op4Async(
            short[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<ReadOnlyMemory<ushort>> Op5Async(
            ushort[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<ReadOnlyMemory<int>> Op6Async(
            int[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<ReadOnlyMemory<uint>> Op7Async(
            uint[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<int>> Op8Async(
            int[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<uint>> Op9Async(
            uint[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);
        public ValueTask<ReadOnlyMemory<long>> Op10Async(
            long[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<ReadOnlyMemory<ulong>> Op11Async(
            ulong[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<long>> Op12Async(
            long[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<ulong>> Op13Async(
            ulong[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<ReadOnlyMemory<float>> Op14Async(
            float[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<ReadOnlyMemory<double>> Op15Async(
            double[] a,
            Dispatch dispatch,
            CancellationToken cancel = default) => new(a);

        public ValueTask<IEnumerable<string>> Op16Async(
            string[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);
    }

    class SequenceWithOptionalValueOperations : Service, ISequenceWithOptionalValueOperations
    {
        public ValueTask<IEnumerable<bool?>> Op1Async(
            bool?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<sbyte?>> Op2Async(
            sbyte?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<byte?>> Op3Async(
            byte?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<short?>> Op4Async(
            short?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<ushort?>> Op5Async(
            ushort?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<int?>> Op6Async(
            int?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<uint?>> Op7Async(
            uint?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<int?>> Op8Async(
            int?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<uint?>> Op9Async(
            uint?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<long?>> Op10Async(
            long?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<ulong?>> Op11Async(
            ulong?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<long?>> Op12Async(
            long?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<ulong?>> Op13Async(
            ulong?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<float?>> Op14Async(
            float?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<double?>> Op15Async(
            double?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<string?>> Op16Async(
            string?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);
    }

    [Test]
    public async Task Sequence_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new SequenceOperations())
            .BuildServiceProvider();

        var prx = SequenceOperationsPrx.FromConnection(provider.GetRequiredService<Connection>(), "/service");

        var p1 = Enumerable.Range(0, 100).Select(i => i % 2 == 0).ToArray();
        var r1 = await prx.Op1Async(p1);
        Assert.That(r1, Is.EqualTo(p1));

        var p2 = Enumerable.Range(0, 100).Select(i => (sbyte)i).ToArray();
        var r2 = await prx.Op2Async(p2);
        Assert.That(r2, Is.EqualTo(p2));

        var p3 = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        var r3 = await prx.Op3Async(p3);
        Assert.That(r3, Is.EqualTo(p3));

        var p4 = Enumerable.Range(0, 100).Select(i => (short)i).ToArray();
        var r4 = await prx.Op4Async(p4);
        Assert.That(r4, Is.EqualTo(p4));

        var p5 = Enumerable.Range(0, 100).Select(i => (ushort)i).ToArray();
        var r5 = await prx.Op5Async(p5);
        Assert.That(r5, Is.EqualTo(p5));

        var p6 = Enumerable.Range(0, 100).Select(i => i).ToArray();
        var r6 = await prx.Op6Async(p6);
        Assert.That(r6, Is.EqualTo(p6));

        var p7 = Enumerable.Range(0, 100).Select(i => (uint)i).ToArray();
        var r7 = await prx.Op7Async(p7);
        Assert.That(r7, Is.EqualTo(p7));

        var p8 = Enumerable.Range(0, 100).Select(i => i).ToArray();
        var r8 = await prx.Op8Async(p8);
        Assert.That(r8, Is.EqualTo(p8));

        var p9 = Enumerable.Range(0, 100).Select(i => (uint)i).ToArray();
        var r9 = await prx.Op9Async(p9);
        Assert.That(r9, Is.EqualTo(p9));

        var p10 = Enumerable.Range(0, 100).Select(i => (long)i).ToArray();
        var r10 = await prx.Op10Async(p10);
        Assert.That(r10, Is.EqualTo(p10));

        var p11 = Enumerable.Range(0, 100).Select(i => (ulong)i).ToArray();
        var r11 = await prx.Op11Async(p11);
        Assert.That(r11, Is.EqualTo(p11));

        var p12 = Enumerable.Range(0, 100).Select(i => (long)i).ToArray();
        var r12 = await prx.Op12Async(p12);
        Assert.That(r12, Is.EqualTo(p12));

        var p13 = Enumerable.Range(0, 100).Select(i => (ulong)i).ToArray();
        var r13 = await prx.Op13Async(p13);
        Assert.That(r13, Is.EqualTo(p13));

        var p14 = Enumerable.Range(0, 100).Select(i => (float)i).ToArray();
        var r14 = await prx.Op14Async(p14);
        Assert.That(r14, Is.EqualTo(p14));

        var p15 = Enumerable.Range(0, 100).Select(i => (double)i).ToArray();
        var r15 = await prx.Op15Async(p15);
        Assert.That(r15, Is.EqualTo(p15));

        var p16 = Enumerable.Range(0, 100).Select(i => $"value-{i}").ToArray();
        var r16 = await prx.Op16Async(p16);
        Assert.That(r16, Is.EqualTo(p16));
    }

    [Test]
    public async Task Sequence_of_optional_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new SequenceWithOptionalValueOperations())
            .BuildServiceProvider();

        var prx = SequenceWithOptionalValueOperationsPrx.FromConnection(provider.GetRequiredService<Connection>(), "/service");

        var p1 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? true : (bool?)null).ToArray();
        var r1 = await prx.Op1Async(p1);
        Assert.That(r1, Is.EqualTo(p1));

        var p2 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (sbyte?)i : null).ToArray();
        var r2 = await prx.Op2Async(p2);
        Assert.That(r2, Is.EqualTo(p2));

        var p3 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (byte?)i : null).ToArray();
        var r3 = await prx.Op3Async(p3);
        Assert.That(r3, Is.EqualTo(p3));

        var p4 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (short?)i : null).ToArray();
        var r4 = await prx.Op4Async(p4);
        Assert.That(r4, Is.EqualTo(p4));

        var p5 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (ushort?)i : null).ToArray();
        var r5 = await prx.Op5Async(p5);
        Assert.That(r5, Is.EqualTo(p5));

        var p6 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (int?)i : null).ToArray();
        var r6 = await prx.Op6Async(p6);
        Assert.That(r6, Is.EqualTo(p6));

        var p7 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (uint?)i : null).ToArray();
        var r7 = await prx.Op7Async(p7);
        Assert.That(r7, Is.EqualTo(p7));

        var p8 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (int?)i : null).ToArray();
        var r8 = await prx.Op8Async(p8);
        Assert.That(r8, Is.EqualTo(p8));

        var p9 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (uint?)i : null).ToArray();
        var r9 = await prx.Op9Async(p9);
        Assert.That(r9, Is.EqualTo(p9));

        var p10 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (long?)i : null).ToArray();
        var r10 = await prx.Op10Async(p10);
        Assert.That(r10, Is.EqualTo(p10));

        var p11 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (ulong?)i : null).ToArray();
        var r11 = await prx.Op11Async(p11);
        Assert.That(r11, Is.EqualTo(p11));

        var p12 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (long?)i : null).ToArray();
        var r12 = await prx.Op12Async(p12);
        Assert.That(r12, Is.EqualTo(p12));

        var p13 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (ulong?)i : null).ToArray();
        var r13 = await prx.Op13Async(p13);
        Assert.That(r13, Is.EqualTo(p13));

        var p14 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (float?)i : null).ToArray();
        var r14 = await prx.Op14Async(p14);
        Assert.That(r14, Is.EqualTo(p14));

        var p15 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (double?)i : null).ToArray();
        var r15 = await prx.Op15Async(p15);
        Assert.That(r15, Is.EqualTo(p15));

        var p16 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? (string?)$"value-{i}" : null).ToArray();
        var r16 = await prx.Op16Async(p16);
        Assert.That(r16, Is.EqualTo(p16));
    }

    [Test]
    public async Task Sequence_op_optionals_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new SequenceOperations())
            .BuildServiceProvider();

        var prx = SequenceOperationsPrx.FromConnection(provider.GetRequiredService<Connection>(), "/service");

        var p1 = Enumerable.Range(0, 100).Select(i => i % 2 == 0).ToArray();
        var r1 = await prx.Op1Async(p1);
        Assert.That(r1, Is.EqualTo(p1));

        var p2 = Enumerable.Range(0, 100).Select(i => (sbyte)i).ToArray();
        var r2 = await prx.Op2Async(p2);
        Assert.That(r2, Is.EqualTo(p2));

        var p3 = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        var r3 = await prx.Op3Async(p3);
        Assert.That(r3, Is.EqualTo(p3));

        var p4 = Enumerable.Range(0, 100).Select(i => (short)i).ToArray();
        var r4 = await prx.Op4Async(p4);
        Assert.That(r4, Is.EqualTo(p4));

        var p5 = Enumerable.Range(0, 100).Select(i => (ushort)i).ToArray();
        var r5 = await prx.Op5Async(p5);
        Assert.That(r5, Is.EqualTo(p5));

        var p6 = Enumerable.Range(0, 100).Select(i => i).ToArray();
        var r6 = await prx.Op6Async(p6);
        Assert.That(r6, Is.EqualTo(p6));

        var p7 = Enumerable.Range(0, 100).Select(i => (uint)i).ToArray();
        var r7 = await prx.Op7Async(p7);
        Assert.That(r7, Is.EqualTo(p7));

        var p8 = Enumerable.Range(0, 100).Select(i => i).ToArray();
        var r8 = await prx.Op8Async(p8);
        Assert.That(r8, Is.EqualTo(p8));

        var p9 = Enumerable.Range(0, 100).Select(i => (uint)i).ToArray();
        var r9 = await prx.Op9Async(p9);
        Assert.That(r9, Is.EqualTo(p9));

        var p10 = Enumerable.Range(0, 100).Select(i => (long)i).ToArray();
        var r10 = await prx.Op10Async(p10);
        Assert.That(r10, Is.EqualTo(p10));

        var p11 = Enumerable.Range(0, 100).Select(i => (ulong)i).ToArray();
        var r11 = await prx.Op11Async(p11);
        Assert.That(r11, Is.EqualTo(p11));

        var p12 = Enumerable.Range(0, 100).Select(i => (long)i).ToArray();
        var r12 = await prx.Op12Async(p12);
        Assert.That(r12, Is.EqualTo(p12));

        var p13 = Enumerable.Range(0, 100).Select(i => (ulong)i).ToArray();
        var r13 = await prx.Op13Async(p13);
        Assert.That(r13, Is.EqualTo(p13));

        var p14 = Enumerable.Range(0, 100).Select(i => (float)i).ToArray();
        var r14 = await prx.Op14Async(p14);
        Assert.That(r14, Is.EqualTo(p14));

        var p15 = Enumerable.Range(0, 100).Select(i => (double)i).ToArray();
        var r15 = await prx.Op15Async(p15);
        Assert.That(r15, Is.EqualTo(p15));

        var p16 = Enumerable.Range(0, 100).Select(i => $"value-{i}").ToArray();
        var r16 = await prx.Op16Async(p16);
        Assert.That(r16, Is.EqualTo(p16));
    }
}
