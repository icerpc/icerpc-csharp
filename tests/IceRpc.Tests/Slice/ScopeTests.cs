// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Configure;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using System.Buffers;
using System.IO.Pipelines;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ScopeTests
{
    [Test]
    public async Task Operation_with_parameter_defined_in_different_scopes1()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new ScopeOperations1())
            .BuildServiceProvider();
        var prx = ScopeOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        var r = await prx.OpAsync(new S(10));

        Assert.That(r.V, Is.EqualTo(10));
    }

    [Test]
    public async Task Operation_with_parameter_defined_in_different_scopes2()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new ScopeOperations2())
            .BuildServiceProvider();
        var prx = Inner.ScopeOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        var r = await prx.OpAsync(new Inner.S(10));

        Assert.That(r.V, Is.EqualTo(10));
    }

    public class ScopeOperations1 : Service, IScopeOperations
    {
        public ValueTask<Inner.S> OpAsync(S s, Dispatch dispatch, CancellationToken cancel = default) =>
            new(new Inner.S(s.V));
    }

    public class ScopeOperations2 : Service, Inner.IScopeOperations
    {
        public ValueTask<S> OpAsync(
            Inner.S s,
            Dispatch dispatch,
            CancellationToken cancel) => new(new S(s.V));
    }
}
