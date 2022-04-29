// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests.Slice1;

public class EnumTests
{
    public class MyEnumOperations : Service, IMyEnumOperations
    {
        public ValueTask<MyEnumA> Op1Async(
            MyEnumA p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);
        public ValueTask<MyEnumB> Op2Async(
            MyEnumB p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);
    }

    [Test]
    public async Task Enum_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyEnumOperations())
            .BuildServiceProvider();

        var prx = MyEnumOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        var a = await prx.Op1Async(MyEnumA.Two);
        Assert.That(a, Is.EqualTo(MyEnumA.Two));

        var b = await prx.Op2Async(MyEnumB.Twenty);
        Assert.That(b, Is.EqualTo(MyEnumB.Twenty));
    }
}
