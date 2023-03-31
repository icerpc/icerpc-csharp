// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Tests.Slice.FileScopeNamespaceAttribute.WithFileScopeNamespace;

[Parallelizable(scope: ParallelScope.All)]
public class FileScopeNamespaceAttributeTests
{
    [Test]
    public async Task Operation_with_types_using_cs_namespace_attribute()
    {
        // Arrange
        var invoker = new ColocInvoker(new FileScopeNamespaceOperationsService());
        var proxy = new FileScopeNamespaceOperationsProxy(invoker);

        // Act
        S1 r = await proxy.Op1Async(new S1("10"));

        // Assert
        Assert.That(r.I, Is.EqualTo("10"));
    }

    private sealed class FileScopeNamespaceOperationsService : Service, IFileScopeNamespaceOperationsService
    {
        public ValueTask<S1> Op1Async(S1 p, IFeatureCollection features, CancellationToken cancellationToken) =>
            new(p);
    }
}
