// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests.Slice1;

public class DictionaryTests
{
    class DictionaryOperations : Service, IDictionaryOperations
    {
        public ValueTask<IEnumerable<KeyValuePair<int, AnyClass>>> Op1Async(
            Dictionary<int, AnyClass> a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);

        public ValueTask<IEnumerable<KeyValuePair<int, AnyClass?>>> Op2Async(
            Dictionary<int, AnyClass?> a,
            Dispatch dispatch,
            CancellationToken cancel = default) => new(a);
    }

    [Test]
    public async Task Dictionary_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new DictionaryOperations())
            .BuildServiceProvider();

        var prx = DictionaryOperationsPrx.FromConnection(provider.GetRequiredService<Connection>(), "/service");

        var p1 = Enumerable.Range(0, 10).ToDictionary(x => x, x => (AnyClass)new MyClassA(x));
        var r1 = await prx.Op1Async(p1);
        Assert.That(r1.Count, Is.EqualTo(p1.Count));
        foreach (KeyValuePair<int, AnyClass> kvp1 in r1)
        {
            var value = r1[kvp1.Key];
            Assert.That(value, Is.TypeOf<MyClassA>());
            Assert.That(((MyClassA)value).X, Is.EqualTo(kvp1.Key));
        }

        var p2 = Enumerable.Range(0, 2).ToDictionary(
            x => x,
            x => x % 2 == 0 ? (AnyClass?)new MyClassA(x) : null);
        var r2 = await prx.Op2Async(p2);
        Assert.That(r2.Count, Is.EqualTo(p2.Count));
        foreach (KeyValuePair<int, AnyClass?> kvp2 in r2)
        {
            if(r2[kvp2.Key] is AnyClass value)
            {
                Assert.That(value, Is.TypeOf<MyClassA>());
                Assert.That(((MyClassA)value).X, Is.EqualTo(kvp2.Key));
            }
        }
    }
}
