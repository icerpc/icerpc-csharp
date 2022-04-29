// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

public class CompactStructTests
{
    public class MyCompactStructOperations : Service, ICompactStructOperations
    {
        public ValueTask<MyCompactStructA> Op1Async(
            MyCompactStructA p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);

        public ValueTask<MyCompactStructWithOptionalsA> Op2Async(
            MyCompactStructWithOptionalsA p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);
    }

    [Test]
    public async Task Compact_struct_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyCompactStructOperations())
            .BuildServiceProvider();

        var prx = CompactStructOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        var aExpected = new MyCompactStructA(
            true,
            sbyte.MaxValue,
            byte.MaxValue,
            short.MaxValue,
            ushort.MaxValue,
            int.MaxValue,
            uint.MaxValue,
            int.MaxValue,
            uint.MaxValue,
            long.MaxValue,
            ulong.MaxValue,
            SliceEncoder.VarInt62MaxValue,
            SliceEncoder.VarInt62MaxValue,
            float.MaxValue,
            double.MaxValue,
            "hello world!");

        var a = await prx.Op1Async(aExpected);
        Assert.That(a, Is.EqualTo(aExpected));

        var bExpected = new MyCompactStructWithOptionalsA();
        var b = await prx.Op2Async(new MyCompactStructWithOptionalsA());
        Assert.That(b, Is.EqualTo(bExpected));

        b = new MyCompactStructWithOptionalsA(
            true,
            sbyte.MaxValue,
            byte.MaxValue,
            short.MaxValue,
            ushort.MaxValue,
            int.MaxValue,
            uint.MaxValue,
            int.MaxValue,
            uint.MaxValue,
            long.MaxValue,
            ulong.MaxValue,
            SliceEncoder.VarInt62MaxValue,
            SliceEncoder.VarInt62MaxValue,
            float.MaxValue,
            double.MaxValue,
            "hello world!");

        b = await prx.Op2Async(new MyCompactStructWithOptionalsA());
        Assert.That(b, Is.EqualTo(bExpected));

        b = new MyCompactStructWithOptionalsA(
            true,
            null,
            byte.MaxValue,
            null,
            ushort.MaxValue,
            null,
            uint.MaxValue,
            null,
            uint.MaxValue,
            null,
            ulong.MaxValue,
            null,
            SliceEncoder.VarInt62MaxValue,
            null,
            double.MaxValue,
            null);

        b = await prx.Op2Async(new MyCompactStructWithOptionalsA());
        Assert.That(b, Is.EqualTo(bExpected));
    }
}
