// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests.Slice1;

public class ClassTests
{
    public class MyClassOperations : Service, IMyClassOperations
    {
        public ValueTask<MyClassA> Op1Async(MyClassA p1, Dispatch dispatch, CancellationToken cancel) => new(p1);
        public ValueTask<MyClassB> Op2Async(MyClassB p1, Dispatch dispatch, CancellationToken cancel) => new(p1);
    }

    [Test]
    public async Task Class_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyClassOperations())
            .BuildServiceProvider();

        var prx = MyClassOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        var a = await prx.Op1Async(new MyClassA(10));
        Assert.That(a.X, Is.EqualTo(10));
        a = await prx.Op1Async(new MyClassB(10, 20));
        Assert.That(a.X, Is.EqualTo(10));
        Assert.That(a, Is.TypeOf<MyClassB>());
        var b = (MyClassB)a;
        Assert.That(b.Y, Is.EqualTo(20));
        b = await prx.Op2Async(new MyClassB(10, 20));
        Assert.That(b.X, Is.EqualTo(10));
        Assert.That(b.Y, Is.EqualTo(20));
    }
}
