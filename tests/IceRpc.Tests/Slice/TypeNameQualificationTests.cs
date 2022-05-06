// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class TypeNameQualificationTests
{
    /// <summary>Verifies that when a type is defined in multiple modules, the generated code doesn't mix up the
    /// type names, and use the correct qualified type names.</summary>
    [Test]
    public async Task Operation_with_parameter_type_name_defined_in_multiple_modules()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new TypeNameQualification())
            .BuildServiceProvider();
        var prx = TypeNameQualificationOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        var r = await prx.OpWithTypeNamesDefinedInMultipleModulesAsync(new Inner.S(10));

        Assert.That(r.V, Is.EqualTo("10"));
    }

    public class TypeNameQualification : Service, ITypeNameQualificationOperations
    {
        public ValueTask<S> OpWithTypeNamesDefinedInMultipleModulesAsync(
            Inner.S s,
            Dispatch dispatch,
            CancellationToken cancel) => new(new S($"{s.V}"));
    }
}
