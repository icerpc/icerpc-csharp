// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Ice.Generator.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class ServiceTests
{
    [Test]
    public void Operation_without_parameters_and_void_return()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyMyMoreDerivedService());
        var proxy = new MyMoreDerivedProxy(invoker);

        // Act/Assert
        Assert.That(async () => await proxy.Op3Async(), Throws.Nothing);
    }
}

[Service]
internal partial class MyMyBaseService : IMyBaseService
{
    public ValueTask Op1Async(IFeatureCollection features, CancellationToken cancellationToken) => default;
}

// This class doesn't use the Service attribute, and no dispatch method is generated for it.
internal partial class MyMyDerivedService : MyMyBaseService, IMyDerivedService
{
    public ValueTask Op2Async(IFeatureCollection features, CancellationToken cancellationToken) => default;
}

[Service]
internal partial class MyMyMoreDerivedService : MyMyDerivedService, IMyMoreDerivedService
{
    public ValueTask Op3Async(IFeatureCollection features, CancellationToken cancellationToken) => default;
}
