// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

public class ExceptionTests
{
    public class MyEnumOperations : Service, IExceptionOperations
    {
        public ValueTask<ExceptionA> Op1Async(ExceptionA p1, Dispatch dispatch, CancellationToken cancel) => new(p1);
        public ValueTask<ExceptionB> Op2Async(ExceptionB p1, Dispatch dispatch, CancellationToken cancel) => new(p1);
        public ValueTask ThrowAAsync(ExceptionA p1, Dispatch dispatch, CancellationToken cancel) =>
            throw new ExceptionA(p1.A);
        public ValueTask ThrowBAsync(ExceptionB p1, Dispatch dispatch, CancellationToken cancel) =>
            throw new ExceptionB(new ExceptionA(p1.Inner.A));
    }

    [Test]
    public async Task Exception_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyEnumOperations())
            .BuildServiceProvider();

        var prx = ExceptionOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        var a = await prx.Op1Async(new ExceptionA(10));
        Assert.That(a.A, Is.EqualTo(10));

        var b = await prx.Op2Async(new ExceptionB(new ExceptionA(20)));
        Assert.That(b.Inner.A, Is.EqualTo(20));

        Assert.That(
            async () => await prx.ThrowAAsync(new ExceptionA(10)),
            Throws.TypeOf<ExceptionA>());
        Assert.That(
            async () => await prx.ThrowBAsync(new ExceptionB(new ExceptionA(20))),
            Throws.TypeOf<ExceptionB>());
    }
}
