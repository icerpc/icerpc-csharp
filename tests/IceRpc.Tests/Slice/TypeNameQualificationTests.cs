// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
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
        await using ServiceProvider provider = new ServiceCollection()
            .AddColocTest()
            .AddSingleton<IDispatcher>(new TypeNameQualification())
            .BuildServiceProvider();

        provider.GetRequiredService<Server>().Listen();
        var prx = TypeNameQualificationOperationsPrx.FromConnection(provider.GetRequiredService<ClientConnection>());

        var r = await prx.OpWithTypeNamesDefinedInMultipleModulesAsync(new Inner.S(10));

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
