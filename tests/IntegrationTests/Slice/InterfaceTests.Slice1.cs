// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests.Slice1;

public class InterfaceTests
{
    public class MyInterface : Service, IMyInterface
    {
        public ValueTask<AnyClass> Op1Async(AnyClass c, Dispatch dispatch, CancellationToken cancel) => new(c);
        public ValueTask<MyClassA> Op2Async(MyClassA c, Dispatch dispatch, CancellationToken cancel) => new(c);
    }

    [Test]
    public async Task MyInterface_operations([Values("/service1", "/service2")] string path)
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyInterface())
            .BuildServiceProvider();

        var prx = MyInterfacePrx.FromConnection(provider.GetRequiredService<Connection>(), path);

        var r1 = await prx.Op1Async(new MyClassA(10));
        Assert.That(r1, Is.TypeOf<MyClassA>());
        Assert.That(((MyClassA)r1).X, Is.EqualTo(10));

        var r2 = await prx.Op2Async(new MyClassA(10));
        Assert.That(r2.X, Is.EqualTo(10));
    }
}
