// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.Slice.Tests;

[Parallelizable(scope: ParallelScope.All)]
public class DictionaryMappingTests
{
    class DictionaryMappingOperations : Service, IDictionaryMappingOperations
    {
        public ValueTask<(IEnumerable<KeyValuePair<int, int>> R1, IEnumerable<KeyValuePair<int, int>> R2)> OpMultipeReturnValuesWithCsGenericAttributeAsync(
            Dispatch dispatch,
            CancellationToken cancel) => new((
                    new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 },
                    new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }
                ));

        public ValueTask OpParameterWithCsGenericAttributeAsync(
            CustomDictionary<int, int> p,
            Dispatch dispatch,
            CancellationToken cancel) => default;

        public ValueTask<IEnumerable<KeyValuePair<int, int>>> OpSingleReturnValueWithCsGenericAttributeAsync(
            Dispatch dispatch,
            CancellationToken cancel) => new(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 });
    }

    [Test]
    public async Task Operation_with_multiple_return_values_and_cs_generic_attribute()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new DictionaryMappingOperations())
            .BuildServiceProvider();
        var prx = DictionaryMappingOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        (CustomDictionary<int, int> r1, CustomDictionary<int, int> r2) =
            await prx.OpMultipeReturnValuesWithCsGenericAttributeAsync();

        Assert.That(r1, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
        Assert.That(r2, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
    }

    [Test]
    public async Task Operation_with_single_return_value_and_cs_generic_attribute()
    {
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new DictionaryMappingOperations())
            .BuildServiceProvider();
        var prx = DictionaryMappingOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        // TODO bogus mapping, this should return CustomDictionary<int, int>
        Dictionary<int, int> r = await prx.OpSingleReturnValueWithCsGenericAttributeAsync();

        Assert.That(r, Is.EqualTo(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
    }

    [Test]
    public async Task Operation_parameter_with_cs_generic_attribute()
    {
        // Arrange
        await using var provider = new SliceTestServiceCollection()
            .UseDispatcher(new DictionaryMappingOperations())
            .BuildServiceProvider();
        var prx = DictionaryMappingOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        // Act/Assert
        Assert.That(
            async () => await prx.OpParameterWithCsGenericAttributeAsync(
                new CustomDictionary<int, int>(new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 })),
            Throws.Nothing);
    }
}
