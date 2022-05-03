// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

public class EnumTests
{
    /// <summary>Verify that we can use an enum as a input parameter and a return value.</summary>
    [Test]
    public async Task Enum_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyEnumOperations())
            .BuildServiceProvider();
        var prx = MyEnumOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        var a = await prx.Op1Async(MyEnumA.Two);

        Assert.That(a, Is.EqualTo(MyEnumA.Two));
    }

    /// <summary>Verify that we can use an enumerator not defined in Slice with an unchecked enum parameter and return
    /// value.</summary>
    [Test]
    public async Task Unchecked_enum_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyEnumOperations())
            .BuildServiceProvider();

        var prx = MyEnumOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        var a = await prx.Op2Async((MyEnumB)30);

        Assert.That(a, Is.EqualTo((MyEnumB)30));
    }

    /// <summary>Verifies that receiving a enumerator value that is not valid fails with
    /// <see cref="InvalidDataException"/>.</summary>
    [Test]
    public async Task Checked_enum_failure()
    {
        // Arrange
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyEnumOperations())
            .BuildServiceProvider();

        var prx = MyEnumOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        // Act/Assert
        Assert.That(async () => await prx.Op3Async(), Throws.TypeOf<InvalidDataException>());
    }

    /// <summary>Verifies that we can use an enum as a stream input parameter and a stream return value.</summary>
    [Test]
    public async Task Enum_stream_operations()
    {
        // Arrange
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new MyEnumOperations())
            .BuildServiceProvider();
        var prx = MyEnumOperationsPrx.FromConnection(provider.GetRequiredService<Connection>());

        // Act
        var r1 = await prx.Op4Async(GetDataAsync());

        // Assert
        var enumerator = r1.GetAsyncEnumerator();
        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(MyEnumA.One));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(MyEnumA.Two));

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(MyEnumA.Three));

        Assert.That(await enumerator.MoveNextAsync(), Is.False);

        static async IAsyncEnumerable<MyEnumA> GetDataAsync()
        {
            await Task.Yield();
            yield return MyEnumA.One;
            yield return MyEnumA.Two;
            yield return MyEnumA.Three;
        }
    }

    private class MyEnumOperations : Service, IMyEnumOperations
    {
        public ValueTask<MyEnumA> Op1Async(
            MyEnumA p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);

        public ValueTask<MyEnumB> Op2Async(
            MyEnumB p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);

        public ValueTask<MyEnumA> Op3Async(
            Dispatch dispatch,
            CancellationToken cancel) => new((MyEnumA)25);

        public ValueTask<IAsyncEnumerable<MyEnumA>> Op4Async(
            IAsyncEnumerable<MyEnumA> p1, 
            Dispatch dispatch, 
            CancellationToken cancel = default) => new(p1);
    }
}
