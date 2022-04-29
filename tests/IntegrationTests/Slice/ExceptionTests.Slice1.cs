// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests.Slice1;

public class ExceptionTests
{
    public class MyExceptionOperations : Service, IExceptionOperations
    {
        public ValueTask ThrowsAAsync(Dispatch dispatch, CancellationToken cancel = default) =>
            throw new ExceptionA(10);

        public ValueTask ThrowsBAsync(Dispatch dispatch, CancellationToken cancel = default) =>
            throw new ExceptionB(10, 20);
    }

    [Test]
    public async Task Exception_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyExceptionOperations())
            .BuildServiceProvider();

        var prx = ExceptionOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        Assert.That(async () => await prx.ThrowsAAsync(), Throws.TypeOf<ExceptionA>());
        Assert.That(async () => await prx.ThrowsBAsync(), Throws.TypeOf<ExceptionB>());
    }
}
