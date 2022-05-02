// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

public class InterfaceTests
{
    public class MyEmptyInterface : Service, IMyEmptyInterface
    {
    }

    public class MyInterface : Service, IMyInterface
    {
        public ValueTask Op1Async(Dispatch dispatch, CancellationToken cancel) => default;
        public ValueTask Op2Async(int x, Dispatch dispatch, CancellationToken cancel) => default;
        public ValueTask<int> Op3Async(Dispatch dispatch, CancellationToken cancel) => new(0);
        public ValueTask<int> Op4Async(int x, Dispatch dispatch, CancellationToken cancel) => new(x);
        public ValueTask<(int X, int Y)> Op5Async(int x, int y, Dispatch dispatch, CancellationToken cancel) => new((x, y));
    }

    public class MyDerivedInterface : MyInterface, IMyDerivedInterface
    {
    }

    public class MyInterfaceWithOptionalParams : Service, IMyInterfaceWithOptionalParams
    {
        public ValueTask<(int? X, int? Y)> Op1Async(int? x, int? y, Dispatch dispatch, CancellationToken cancel) =>
            new((x, y));
    }

    public class MyInterfaceWithEncodedResult : Service, IMyInterfaceWithEncodedResult
    {
        public ValueTask<IMyInterfaceWithEncodedResult.Op1EncodedResult> Op1Async(Dispatch dispatch, CancellationToken cancel) =>
            new(new IMyInterfaceWithEncodedResult.Op1EncodedResult(10, 20));
        public ValueTask<IMyInterfaceWithEncodedResult.Op2EncodedResult> Op2Async(Dispatch dispatch, CancellationToken cancel) =>
            new(new IMyInterfaceWithEncodedResult.Op2EncodedResult(new MyStructA()));
    }

    [Test]
    public async Task MyInterface_operations([Values("/service1", "/service2")] string path)
    {
        var router = new Router();
        router.Map("/service1", new MyInterface());
        router.Map("/service2", new MyDerivedInterface());
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(router)
            .BuildServiceProvider();

        var prx = MyInterfacePrx.FromConnection(provider.GetRequiredService<Connection>(), path);

        await prx.Op1Async();

        await prx.Op2Async(10);

        var r3 = await prx.Op3Async();
        Assert.That(r3, Is.EqualTo(0));

        var r4 = await prx.Op4Async(1024);
        Assert.That(r4, Is.EqualTo(1024));

        var r5 = await prx.Op5Async(10, 20);
        Assert.That(r5, Is.EqualTo((10, 20)));
    }

    [Test]
    public async Task MyInterface_with_optional_params()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyInterfaceWithOptionalParams())
            .BuildServiceProvider();

        var prx = MyInterfaceWithOptionalParamsPrx.FromConnection(provider.GetRequiredService<Connection>());

        var r1 = await prx.Op1Async(10, 20);
        Assert.That(r1, Is.EqualTo((10, 20)));
    }

    [Test]
    public async Task MyInterface_with_encoded_result()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyInterfaceWithEncodedResult())
            .BuildServiceProvider();

        var prx = MyInterfaceWithEncodedResultPrx.FromConnection(provider.GetRequiredService<Connection>());

        var r1 = await prx.Op1Async();
        Assert.That(r1, Is.EqualTo((10, 20)));

        var r2 = await prx.Op2Async();
        Assert.That(r2, Is.EqualTo(new MyStructA()));
    }
}
