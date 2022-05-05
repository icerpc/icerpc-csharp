// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class SequenceMappingTests
{
    class SequenceMappingOperations : Service, ISequenceMappingOperations
    {
        public ValueTask<(IEnumerable<int> R1, IEnumerable<int> R2)> OpMultipeReturnValuesWithCsGenericAttributeAsync(
            Dispatch dispatch,
            CancellationToken cancel) => new((new int[] { 1, 2, 3}, new[] { 1, 2, 3 }));

        public ValueTask OpParameterWithCsGenericAttributeAsync(
            CustomSequence<int> p,
            Dispatch dispatch,
            CancellationToken cancel) => default;

        // TODO bogus mapping, cs::generic should return an IEnumerable
        public ValueTask<ReadOnlyMemory<int>> OpSingleReturnValueWithCsGenericAttributeAsync(
            Dispatch dispatch,
            CancellationToken cancel) => new(new int[] {1, 2, 3});
    }

    [Test]
    public async Task Operation_with_multiple_return_values_and_cs_generic_attribute()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new SequenceMappingOperations())
            .BuildServiceProvider();
        var prx = SequenceMappingOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        (CustomSequence<int> r1, CustomSequence<int> r2) =
            await prx.OpMultipeReturnValuesWithCsGenericAttributeAsync();

        Assert.That(r1, Is.EqualTo(new int[] { 1, 2, 3 }));
        Assert.That(r2, Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Operation_with_single_return_value_and_cs_generic_attribute()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new SequenceMappingOperations())
            .BuildServiceProvider();
        var prx = SequenceMappingOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        // TODO bogus mapping, this should return CustomSequence<int>
        int[] r = await prx.OpSingleReturnValueWithCsGenericAttributeAsync();

        Assert.That(r, Is.EqualTo(new int[] { 1, 2, 3 }));
    }

    [Test]
    public async Task Operation_parameter_with_cs_generic_attribute()
    {
        // Arrange
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new SequenceMappingOperations())
            .BuildServiceProvider();
        var prx = SequenceMappingOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        // Act/Assert
        Assert.That(
            async () => await prx.OpParameterWithCsGenericAttributeAsync(
                new CustomSequence<int>(new int[] { 1, 2, 3 })),
            Throws.Nothing);
    }
}
