// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
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
        // Arrange
        var invoker = new ColocInvoker(new TypeNameQualificationOperationsService());
        var proxy = new TypeNameQualificationOperationsProxy(invoker);

        // Act
        var r = await proxy.OpWithTypeNamesDefinedInMultipleModulesAsync(new Inner.S(10));

        // Assert
        Assert.That(r.V, Is.EqualTo("10"));
    }

    private sealed class TypeNameQualificationOperationsService : Service, ITypeNameQualificationOperationsService
    {
        public ValueTask<S> OpWithTypeNamesDefinedInMultipleModulesAsync(
            Inner.S s,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(new S($"{s.V}"));
    }
}
