// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class TypeNameQualificationTests
{
    [Test]
    public async Task Operation_with_parameter_defined_in_different_scopes()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new ScopeOperations1())
            .BuildServiceProvider();
        var prx = TypeNameQualificationOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        var r = await prx.OpWithTypeNamesDefinedInMultipleModulesAsync(new Inner.S(10));

        Assert.That(r.V, Is.EqualTo(10));
    }

    public class ScopeOperations1 : Service, ITypeNameQualificationOperations
    {
        public ValueTask<S> OpWithTypeNamesDefinedInMultipleModulesAsync(
            Inner.S s,
            Dispatch dispatch,
            CancellationToken cancel) => new(new S(s.V));
    }
}
