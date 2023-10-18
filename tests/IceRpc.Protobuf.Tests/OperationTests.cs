// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc.Features;
using IceRpc.Tests.Common;
using NUnit.Framework;

namespace IceRpc.Protobuf.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class OperationTests
{
    [Test]
    public void Unary_operation_with_empty_param_and_return()
    {
        // Arrange
        var invoker = new ColocInvoker(new MyOperationsService());
        var client = new MyOperationsClient(invoker);

        // Act/Assert
        Assert.That(async () => await client.UnaryOpWithEmptyParamAndReturnAsync(new Empty()), Throws.Nothing);
    }

    [ProtobufService]
    internal partial class MyOperationsService : IMyOperationsService
    {
        ValueTask<Empty> IMyOperationsService.UnaryOpWithEmptyParamAndReturnAsync(
            Empty message,
            IFeatureCollection? features,
            CancellationToken cancellationToken) => new(new Empty());
    }
}
