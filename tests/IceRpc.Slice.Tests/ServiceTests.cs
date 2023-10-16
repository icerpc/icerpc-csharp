// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class ServiceTests
{
    [Test]
    public void Operation_without_parameters_and_void_return()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyMoreDerivedService());
        var proxy = new MoreDerivedProxy(invoker);

        // Act/Assert
        Assert.That(async () => await proxy.Op2Async(), Throws.Nothing);
    }
}

[SliceService]
internal partial class MyBaseService : IBaseService
{
    public ValueTask Op1Async(IFeatureCollection features, CancellationToken cancellationToken) => default;
}

// This class doesn't use the SliceService attribute, and no dispatch method would be generated for it. It would
// be possible to call `IDerivedService` using the code generated for the derived class `MyMoreDerivedService`.
internal class MyDerivedService : MyBaseService, IDerivedService
{
    public ValueTask Op2Async(IFeatureCollection features, CancellationToken cancellationToken) => default;
}

[SliceService]
internal partial class MyMoreDerivedService : MyDerivedService, IMoreDerivedService
{
    public ValueTask Op3Async(IFeatureCollection features, CancellationToken cancellationToken) => default;
}
