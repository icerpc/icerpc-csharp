// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ScopeTests
{
    [Test]
    public async Task Operation_with_parameter_defined_in_different_scopes()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new ScopeOperations1())
            .BuildServiceProvider();
        var prx = ScopeOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        var r = await prx.OpWithParameterDefinedInDifferentScopeAsync(new Inner.S(10));

        Assert.That(r.V, Is.EqualTo(10));
    }

    [Test]
    public async Task Operation_with_return_defined_in_different_scopes()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new ScopeOperations1())
            .BuildServiceProvider();
        var prx = ScopeOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        var r = await prx.OpWithReturnDefinedInDifferentScopeAsync(new S(10));

        Assert.That(r.V, Is.EqualTo(10));
    }

    public class ScopeOperations1 : Service, IScopeOperations
    {
        public ValueTask<S> OpWithParameterDefinedInDifferentScopeAsync(
            Inner.S s,
            Dispatch dispatch,
            CancellationToken cancel) => new(new S(s.V));

        public ValueTask<Inner.S> OpWithReturnDefinedInDifferentScopeAsync(
            S s,
            Dispatch dispatch,
            CancellationToken cancel) => new(new Inner.S(s.V));
    }
}
