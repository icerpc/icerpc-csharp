// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Tests.Slice;

[Parallelizable(scope: ParallelScope.All)]
public class TypeNameQualificationTests
{
    /// <summary>Verifies that when a type is defined in multiple modules, the generated code doesn't mix up the
    /// type names, and use the correct qualified type names.</summary>
    [Test]
    public async Task Operation_with_parameter_type_name_defined_in_multiple_modules()
    {
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest(new TypeNameQualification())
            .AddIceRpcProxy<ITypeNameQualificationOperationsPrx, TypeNameQualificationOperationsPrx>()
            .BuildServiceProvider(validateScopes: true);

        ITypeNameQualificationOperationsPrx proxy = provider.GetRequiredService<ITypeNameQualificationOperationsPrx>();
        provider.GetRequiredService<Server>().Listen();

        var r = await proxy.OpWithTypeNamesDefinedInMultipleModulesAsync(new Inner.S(10));

        Assert.That(r.V, Is.EqualTo("10"));
    }

    private class TypeNameQualification : Service, ITypeNameQualificationOperations
    {
        public ValueTask<S> OpWithTypeNamesDefinedInMultipleModulesAsync(
            Inner.S s,
            IFeatureCollection features,
            CancellationToken cancel) => new(new S($"{s.V}"));
    }
}
