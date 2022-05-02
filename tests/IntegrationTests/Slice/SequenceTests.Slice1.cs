// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests.Slice1;

public class SequenceTests
{
    class SequenceOperations : Service, ISequenceOperations
    {
        public ValueTask<IEnumerable<AnyClass>> Op1Async(
            AnyClass[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);
        public ValueTask<IEnumerable<AnyClass?>> Op2Async(
            AnyClass?[] a,
            Dispatch dispatch,
            CancellationToken cancel) => new(a);
    }

    [Test]
    public async Task Sequence_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new SequenceOperations())
            .BuildServiceProvider();

        var prx = SequenceOperationsPrx.FromConnection(provider.GetRequiredService<Connection>(), "/service");

        var p1 = Enumerable.Range(0, 100).Select(i => new MyClassA(i)).ToArray();
        var r1= await prx.Op1Async(p1);
        Assert.That(p1.Select(i => i.X), Is.EqualTo(r1.Select(i => ((MyClassA)i).X)));

        var p2 = Enumerable.Range(0, 100).Select(i => i % 2 == 0 ? new MyClassA(i) : null).ToArray();
        var r2 = await prx.Op2Async(p2);
        Assert.That(
            p1.Select(i => i?.X),
            Is.EqualTo(r1.Select(i => i == null ? (int?)null : ((MyClassA)i).X)));
    }
}
