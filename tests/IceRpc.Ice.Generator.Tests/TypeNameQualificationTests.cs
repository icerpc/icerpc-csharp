// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Ice.Generator.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class TypeNameQualificationTests
{
    /// <summary>Verifies that different types with the same name are defined in multiple modules, the generated code
    /// doesn't mix up the type names, and use the correct qualified type names.</summary>
    [Test]
    public async Task Operation_with_parameter_type_defined_in_other_module()
    {
        // Arrange
        var invoker = new ColocInvoker(new TypeNameQualificationOperationsService());
        var proxy = new TypeNameQualificationOperationsProxy(invoker);

        // Act
        var r = await proxy.OpWithTypeDefinedInOtherModuleAsync(new Inner.S(10));

        // Assert
        Assert.That(r.V, Is.EqualTo("10"));
    }

    [Service]
    private sealed partial class TypeNameQualificationOperationsService : ITypeNameQualificationOperationsService
    {
        public ValueTask<S> OpWithTypeDefinedInOtherModuleAsync(
            Inner.S s,
            IFeatureCollection features,
            CancellationToken cancellationToken) => new(new S($"{s.V}"));
    }
}
