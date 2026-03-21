// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Slice.Generator.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServiceTests
{
    [Test]
    public void Operation_without_parameters_and_void_return()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyMoreDerivedService());
        var proxy = new MoreDerivedProxy(invoker);

        // Act/Assert
        Assert.That(async () => await proxy.Op3Async(), Throws.Nothing);
    }
}

[Service]
internal partial class MyBaseService : IBaseService
{
    public ValueTask Op1Async(IFeatureCollection features, CancellationToken cancellationToken) => default;
}

// This class doesn't use the Service attribute, and no dispatch method is generated for it.
internal partial class MyDerivedService : MyBaseService, IDerivedService
{
    public ValueTask Op2Async(IFeatureCollection features, CancellationToken cancellationToken) => default;
}

[Service]
internal partial class MyMoreDerivedService : MyDerivedService, IMoreDerivedService
{
    public ValueTask Op3Async(IFeatureCollection features, CancellationToken cancellationToken) => default;
}
