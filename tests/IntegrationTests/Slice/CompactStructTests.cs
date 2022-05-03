// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

public class CompactStructTests
{
    public class MyCompactStructOperations : Service, ICompactStructOperations
    {
        public ValueTask<(MyCompactStructA R1, MyCompactStructB R2)> Op1Async(
            MyCompactStructA p1,
            MyCompactStructB p2,
            Dispatch dispatch,
            CancellationToken cancel) => new((p1, p2));

        public ValueTask<IAsyncEnumerable<MyCompactStructB>> Op2Async(
            IAsyncEnumerable<MyCompactStructB> p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);
    }

    public static IEnumerable<TestCaseData> CompactStructOperationsSource
    {
        get
        {
            var a = new MyCompactStructA(int.MaxValue, "hello world!");
            yield return new TestCaseData(a, new MyCompactStructB(a, a, "hello world!"));
            yield return new TestCaseData(a, new MyCompactStructB(a, null, "hello world!"));
            yield return new TestCaseData(a, new MyCompactStructB(a, a, null));
        }
    }

    [Test, TestCaseSource(nameof(CompactStructOperationsSource))]
    public async Task Compact_struct_operations(MyCompactStructA a, MyCompactStructB b)
    {
        // Arrange
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyCompactStructOperations())
            .BuildServiceProvider();
        var prx = CompactStructOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        // Act
        var r = await prx.Op1Async(a, b);

        // Assert
        Assert.That(r.R1, Is.EqualTo(a));
        Assert.That(r.R2, Is.EqualTo(b));
    }

    [Test]
    public async Task Compact_struct_stream_operations()
    {
        // Arrange
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyCompactStructOperations())
            .BuildServiceProvider();
        var prx = CompactStructOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        // Act
        var r = await prx.Op2Async(GetDataAsync());

        // Assert
        var enumerator = r.GetAsyncEnumerator();
        for (int i = 0; i < 1024; ++i)
        {
            Assert.That(await enumerator.MoveNextAsync(), Is.True);
            Assert.That(enumerator.Current, Is.EqualTo(CreateItem(i)));
        }
        Assert.That(await enumerator.MoveNextAsync(), Is.False);

        static async IAsyncEnumerable<MyCompactStructB> GetDataAsync()
        {
            await Task.Yield();
            for (int i = 0; i < 1024; ++i)
            {
                yield return CreateItem(i);
            }
        }

        static MyCompactStructB CreateItem(int i)
        {
            var a = new MyCompactStructA(i, $"value-i");
            return new MyCompactStructB(a, i % 2 == 0 ? a : null, i % 7 == 0 ? $"value-{i}" : null);
        }
    }
}
