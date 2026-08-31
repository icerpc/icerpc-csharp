// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Tests.Common;
using Lower.SnakeCase;
using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

namespace IceRpc.Protobuf.Tests;

[Parallelizable(scope: ParallelScope.All)]
public partial class NameMappingTests
{
    // The types in the Lower.SnakeCase namespace come from sha256hash.proto.
    [Test]
    public async Task Invoke_operation_from_package_without_csharp_namespace_option()
    {
        // Arrange
        var invoker = new ColocInvoker(new HashOperationsService());
        var client = new HashOperationsClient(invoker);

        // Act
        HashMessage response = await client.ComputeHashAsync(new HashMessage { Value = "hello" });

        // Assert
        Assert.That(response.Value, Is.EqualTo("hello"));
    }

    [Service]
    internal partial class HashOperationsService : IHashOperationsService
    {
        public ValueTask<HashMessage> ComputeHashAsync(
            HashMessage message,
            IFeatureCollection? features,
            CancellationToken cancellationToken) => new(message);
    }
}
