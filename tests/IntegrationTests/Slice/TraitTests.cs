// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

public partial record struct MyTraitStruct : IMyTrait
{
}

public class TraitTests
{
    class MyInterfaceWithTraitParams : Service, IMyInterfaceWithTraitParams
    {
        public ValueTask<IMyTrait> Op1Async(
            IMyTrait a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<MyStructWithTraitMember> Op2Async(
            MyStructWithTraitMember a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);
        public ValueTask<MyExceptionWithTraitMember> Op3Async(
            MyExceptionWithTraitMember a,
            Dispatch dispatch,
            CancellationToken cancel = default) =>  new(new MyExceptionWithTraitMember(a.A));
    }

    [Test]
    public async Task Trait_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyInterfaceWithTraitParams())
            .BuildServiceProvider();

        var prx = MyInterfaceWithTraitParamsPrx.FromConnection(provider.GetRequiredService<Connection>(), "/service");

        IMyTrait r1 = await prx.Op1Async(new MyTraitStruct(10, 20));
        Assert.That(r1, Is.TypeOf<MyTraitStruct>());
        var r2 = (MyTraitStruct)r1;
        Assert.That(r2.X, Is.EqualTo(10));
        Assert.That(r2.Y, Is.EqualTo(20));

        var r3 = await prx.Op2Async(new MyStructWithTraitMember(new MyTraitStruct(10, 20)));
        Assert.That(r3.A, Is.TypeOf<MyTraitStruct>());
        r2 = (MyTraitStruct)r3.A;
        Assert.That(r2.X, Is.EqualTo(10));
        Assert.That(r2.Y, Is.EqualTo(20));

        var r4 = await prx.Op3Async(new MyExceptionWithTraitMember(new MyTraitStruct(10, 20)));
        Assert.That(r4.A, Is.TypeOf<MyTraitStruct>());
        r2 = (MyTraitStruct)r4.A;
        Assert.That(r2.X, Is.EqualTo(10));
        Assert.That(r2.Y, Is.EqualTo(20));
    }
}
