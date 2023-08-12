// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class ServiceTests
{
    [Test]
    public void Service_with_duplicate_slice_operations_throws_invalid_operation_exception() =>
        Assert.That(() => new ServiceWithDuplicateOps(), Throws.InvalidOperationException);

    internal class ServiceWithDuplicateOps : Service, IService1Service, IService2Service
    {
        public ValueTask Op1Async(IFeatureCollection features, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public ValueTask Op2Async(IFeatureCollection features, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public ValueTask Op3Async(IFeatureCollection features, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
