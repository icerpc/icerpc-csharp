// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests.Slice1;

public class CompactStructTests
{
    public class MyCompactStructOperations : Service, ICompactStructOperations
    {
        public ValueTask<MyCompactStructA> Op1Async(
            MyCompactStructA p1,
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
            byte.MaxValue,
            short.MaxValue,
            int.MaxValue,
            long.MaxValue,
            float.MaxValue,
            double.MaxValue,
            "hello world!",
            null);

        var a = await prx.Op1Async(aExpected);
        Assert.That(a, Is.EqualTo(aExpected));
    }
}
