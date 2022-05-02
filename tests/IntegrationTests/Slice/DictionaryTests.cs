// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

public class DictionaryTests
{
    class DictionaryOperations : Service, IDictionaryOperations
    {
        public ValueTask<IEnumerable<KeyValuePair<bool, bool>>> Op1Async(
            Dictionary<bool, bool> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<sbyte, sbyte>>> Op2Async(
            Dictionary<sbyte, sbyte> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<byte, byte>>> Op3Async(
            Dictionary<byte, byte> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<short, short>>> Op4Async(
            Dictionary<short, short> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<ushort, ushort>>> Op5Async(
            Dictionary<ushort, ushort> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<int, int>>> Op6Async(
            Dictionary<int, int> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<uint, uint>>> Op7Async(
            Dictionary<uint, uint> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<int, int>>> Op8Async(
            Dictionary<int, int> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<uint, uint>>> Op9Async(
            Dictionary<uint, uint> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<long, long>>> Op10Async(
            Dictionary<long, long> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<ulong, ulong>>> Op11Async(
            Dictionary<ulong, ulong> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<long, long>>> Op12Async(
            Dictionary<long, long> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<ulong, ulong>>> Op13Async(
            Dictionary<ulong, ulong> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<float, float>>> Op14Async(
            Dictionary<float, float> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<double, double>>> Op15Async(
            Dictionary<double, double> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<string, string>>> Op16Async(
            Dictionary<string, string> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);
    }

    class DictionaryWithOptionalValueOperations : Service, IDictionaryWithOptionalValueOperations
    {
        public ValueTask<IEnumerable<KeyValuePair<bool, bool?>>> Op1Async(
            Dictionary<bool, bool?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<sbyte, sbyte?>>> Op2Async(
            Dictionary<sbyte, sbyte?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<byte, byte?>>> Op3Async(
            Dictionary<byte, byte?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<short, short?>>> Op4Async(
            Dictionary<short, short?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<ushort, ushort?>>> Op5Async(
            Dictionary<ushort, ushort?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<int, int?>>> Op6Async(
            Dictionary<int, int?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<uint, uint?>>> Op7Async(
            Dictionary<uint, uint?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<int, int?>>> Op8Async(
            Dictionary<int, int?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<uint, uint?>>> Op9Async(
            Dictionary<uint, uint?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<long, long?>>> Op10Async(
            Dictionary<long, long?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<ulong, ulong?>>> Op11Async(
            Dictionary<ulong, ulong?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<long, long?>>> Op12Async(
            Dictionary<long, long?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<ulong, ulong?>>> Op13Async(
            Dictionary<ulong, ulong?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<float, float?>>> Op14Async(
            Dictionary<float, float?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<double, double?>>> Op15Async(
            Dictionary<double, double?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<string, string?>>> Op16Async(
            Dictionary<string, string?> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

    }

    [Test]
    public async Task Dictionary_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new DictionaryOperations())
            .BuildServiceProvider();

        var prx = DictionaryOperationsPrx.FromConnection(provider.GetRequiredService<Connection>(), "/service");

        var p1 = Enumerable.Range(0, 2).ToDictionary(x => x % 2 == 0, x => x % 2 == 0);
        var r1 = await prx.Op1Async(p1);
        Assert.That(r1, Is.EqualTo(p1));

        var p2 = Enumerable.Range(0, 100).ToDictionary(x => (sbyte)x, x => (sbyte)x);
        var r2 = await prx.Op2Async(p2);
        Assert.That(r2, Is.EqualTo(p2));

        var p3 = Enumerable.Range(0, 100).ToDictionary(x => (byte)x, x => (byte)x);
        var r3 = await prx.Op3Async(p3);
        Assert.That(r3, Is.EqualTo(p3));

        var p4 = Enumerable.Range(0, 100).ToDictionary(x => (short)x, x => (short)x);
        var r4 = await prx.Op4Async(p4);
        Assert.That(r4, Is.EqualTo(p4));

        var p5 = Enumerable.Range(0, 100).ToDictionary(x => (ushort)x, x => (ushort)x);
        var r5 = await prx.Op5Async(p5);
        Assert.That(r5, Is.EqualTo(p5));

        var p6 = Enumerable.Range(0, 100).ToDictionary(x => x, x => x);
        var r6 = await prx.Op6Async(p6);
        Assert.That(r6, Is.EqualTo(p6));

        var p7 = Enumerable.Range(0, 100).ToDictionary(x => (uint)x, x => (uint)x);
        var r7 = await prx.Op7Async(p7);
        Assert.That(r7, Is.EqualTo(p7));

        var p8 = Enumerable.Range(0, 100).ToDictionary(x => x, x => x);
        var r8 = await prx.Op8Async(p8);
        Assert.That(r8, Is.EqualTo(p8));

        var p9 = Enumerable.Range(0, 100).ToDictionary(x => (uint)x, x => (uint)x);
        var r9 = await prx.Op9Async(p9);
        Assert.That(r9, Is.EqualTo(p9));

        var p10 = Enumerable.Range(0, 100).ToDictionary(x => (long)x, x => (long)x);
        var r10 = await prx.Op10Async(p10);
        Assert.That(r10, Is.EqualTo(p10));

        var p11 = Enumerable.Range(0, 100).ToDictionary(x => (ulong)x, x => (ulong)x);
        var r11 = await prx.Op11Async(p11);
        Assert.That(r11, Is.EqualTo(p11));

        var p12 = Enumerable.Range(0, 100).ToDictionary(x => (long)x, x => (long)x);
        var r12 = await prx.Op12Async(p12);
        Assert.That(r12, Is.EqualTo(p12));

        var p13 = Enumerable.Range(0, 100).ToDictionary(x => (ulong)x, x => (ulong)x);
        var r13 = await prx.Op13Async(p13);
        Assert.That(r13, Is.EqualTo(p13));

        var p14 = Enumerable.Range(0, 100).ToDictionary(x => (float)x, x => (float)x);
        var r14 = await prx.Op14Async(p14);
        Assert.That(r14, Is.EqualTo(p14));

        var p15 = Enumerable.Range(0, 100).ToDictionary(x => (double)x, x => (double)x);
        var r15 = await prx.Op15Async(p15);
        Assert.That(r15, Is.EqualTo(p15));

        var p16 = Enumerable.Range(0, 100).ToDictionary(x => $"key-{x}", x => $"value-{x}");
        var r16 = await prx.Op16Async(p16);
        Assert.That(r16, Is.EqualTo(p16));
    }

    [Test]
    public async Task Dictionary_of_optional_values_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new DictionaryWithOptionalValueOperations())
            .BuildServiceProvider();

        var prx = DictionaryWithOptionalValueOperationsPrx.FromConnection(
            provider.GetRequiredService<Connection>(),
            "/service");

        var p1 = Enumerable.Range(0, 2).ToDictionary(x => x % 2 == 0, x => (bool?)(x % 2 == 0));
        var r1 = await prx.Op1Async(p1);
        Assert.That(r1, Is.EqualTo(p1));

        var p2 = Enumerable.Range(0, 100).ToDictionary(x => (sbyte)x, x => x % 2 == 0 ? (sbyte?)x : null);
        var r2 = await prx.Op2Async(p2);
        Assert.That(r2, Is.EqualTo(p2));

        var p3 = Enumerable.Range(0, 100).ToDictionary(x => (byte)x, x => x % 2 == 0 ? (byte?)x : null);
        var r3 = await prx.Op3Async(p3);
        Assert.That(r3, Is.EqualTo(p3));

        var p4 = Enumerable.Range(0, 100).ToDictionary(x => (short)x, x => x % 2 == 0 ? (short?)x : null);
        var r4 = await prx.Op4Async(p4);
        Assert.That(r4, Is.EqualTo(p4));

        var p5 = Enumerable.Range(0, 100).ToDictionary(x => (ushort)x, x => x % 2 == 0 ? (ushort?)x : null);
        var r5 = await prx.Op5Async(p5);
        Assert.That(r5, Is.EqualTo(p5));

        var p6 = Enumerable.Range(0, 100).ToDictionary(x => x, x => x % 2 == 0 ? (int?)x : null);
        var r6 = await prx.Op6Async(p6);
        Assert.That(r6, Is.EqualTo(p6));

        var p7 = Enumerable.Range(0, 100).ToDictionary(x => (uint)x, x => x % 2 == 0 ? (uint?)x : null);
        var r7 = await prx.Op7Async(p7);
        Assert.That(r7, Is.EqualTo(p7));

        var p8 = Enumerable.Range(0, 100).ToDictionary(x => x, x => x % 2 == 0 ? (int?)x : null);
        var r8 = await prx.Op8Async(p8);
        Assert.That(r8, Is.EqualTo(p8));

        var p9 = Enumerable.Range(0, 100).ToDictionary(x => (uint)x, x => x % 2 == 0 ? (uint?)x : null);
        var r9 = await prx.Op9Async(p9);
        Assert.That(r9, Is.EqualTo(p9));

        var p10 = Enumerable.Range(0, 100).ToDictionary(x => (long)x, x => x % 2 == 0 ? (long?)x : null);
        var r10 = await prx.Op10Async(p10);
        Assert.That(r10, Is.EqualTo(p10));

        var p11 = Enumerable.Range(0, 100).ToDictionary(x => (ulong)x, x => x % 2 == 0 ? (ulong?)x : null);
        var r11 = await prx.Op11Async(p11);
        Assert.That(r11, Is.EqualTo(p11));

        var p12 = Enumerable.Range(0, 100).ToDictionary(x => (long)x, x => x % 2 == 0 ? (long?)x : null);
        var r12 = await prx.Op12Async(p12);
        Assert.That(r12, Is.EqualTo(p12));

        var p13 = Enumerable.Range(0, 100).ToDictionary(x => (ulong)x, x => x % 2 == 0 ? (ulong?)x : null);
        var r13 = await prx.Op13Async(p13);
        Assert.That(r13, Is.EqualTo(p13));

        var p14 = Enumerable.Range(0, 100).ToDictionary(x => (float)x, x => x % 2 == 0 ? (float?)x : null);
        var r14 = await prx.Op14Async(p14);
        Assert.That(r14, Is.EqualTo(p14));

        var p15 = Enumerable.Range(0, 100).ToDictionary(x => (double)x, x => x % 2 == 0 ? (double?)x : null);
        var r15 = await prx.Op15Async(p15);
        Assert.That(r15, Is.EqualTo(p15));

        var p16 = Enumerable.Range(0, 100).ToDictionary(
            x => $"key-{x}",
            x => x % 2 == 0 ? (string?)$"value-{x}" : null);
        var r16 = await prx.Op16Async(p16);
        Assert.That(r16, Is.EqualTo(p16));
    }
}
