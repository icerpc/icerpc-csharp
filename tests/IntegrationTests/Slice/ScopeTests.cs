// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

public class ScopeTests
{
    class Operations1 : Service, Scope.IOperations
    {
        public ValueTask<Scope.S> OpSAsync(
            Scope.S p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);

        public ValueTask<IEnumerable<KeyValuePair<string, Scope.S>>> OpSMapAsync(
            Dictionary<string, Scope.S> p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);

        public ValueTask<IEnumerable<Scope.S>> OpSSeqAsync(
            Scope.S[] p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);
    }

    class Operations2 : Service, Scope.Inner.IOperations
    {
        public ValueTask<Scope.Inner.Inner2.S> OpSAsync(
            Scope.Inner.Inner2.S p1,
            Dispatch dispatch,
            CancellationToken cancel ) => new(p1);

        public ValueTask<IEnumerable<KeyValuePair<string, Scope.Inner.Inner2.S>>> OpSMapAsync(
            Dictionary<string, Scope.Inner.Inner2.S> p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);

        public ValueTask<IEnumerable<Scope.Inner.Inner2.S>> OpSSeqAsync(
            Scope.Inner.Inner2.S[] p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);
    }

    class Operations3 : Service, Scope.Inner.Inner2.IOperations
    {
        public ValueTask<Scope.Inner.Inner2.S> OpSAsync(
           Scope.Inner.Inner2.S p1,
           Dispatch dispatch,
           CancellationToken cancel) => new(p1);

        public ValueTask<IEnumerable<KeyValuePair<string, Scope.Inner.Inner2.S>>> OpSMapAsync(
            Dictionary<string, Scope.Inner.Inner2.S> p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);

        public ValueTask<IEnumerable<Scope.Inner.Inner2.S>> OpSSeqAsync(
            Scope.Inner.Inner2.S[] p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);
    }

    class Operations4 : Service, Scope.Inner.Test.Inner2.IOperations
    {
        public ValueTask<Scope.S> OpSAsync(
            Scope.S p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);

        public ValueTask<IEnumerable<KeyValuePair<string, Scope.S>>> OpSMapAsync(
            Dictionary<string, Scope.S> p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);

        public ValueTask<IEnumerable<Scope.S>> OpSSeqAsync(
            Scope.S[] p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);
    }

    [Test]
    public async Task Scope_operations1()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new Operations1())
            .BuildServiceProvider();

        var prx = Scope.OperationsPrx.FromConnection(
            provider.GetRequiredService<Connection>(),
            "/service");

        var p1 = new Scope.S(10);
        var r1 = await prx.OpSAsync(p1);
        Assert.That(r1, Is.EqualTo(p1));

        var p2 = new Dictionary<string, Scope.S>
        {
            ["10"] = new Scope.S(10),
            ["20"] = new Scope.S(20),
        };
        var r2 = await prx.OpSMapAsync(p2);
        Assert.That(r2, Is.EqualTo(p2));

        var p3 = new Scope.S[]
        {
            new Scope.S(10),
            new Scope.S(20),
        };
        var r3 = await prx.OpSSeqAsync(p3);
        Assert.That(r3, Is.EqualTo(p3));
    }

    [Test]
    public async Task Scope_operations2()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new Operations2())
            .BuildServiceProvider();

        var prx = Scope.Inner.OperationsPrx.FromConnection(
            provider.GetRequiredService<Connection>(),
            "/service");

        var p1 = new Scope.Inner.Inner2.S(10);
        var r1 = await prx.OpSAsync(p1);
        Assert.That(r1, Is.EqualTo(p1));

        var p2 = new Dictionary<string, Scope.Inner.Inner2.S>
        {
            ["10"] = new Scope.Inner.Inner2.S(10),
            ["20"] = new Scope.Inner.Inner2.S(20),
        };
        var r2 = await prx.OpSMapAsync(p2);
        Assert.That(r2, Is.EqualTo(p2));

        var p3 = new Scope.Inner.Inner2.S[]
        {
            new Scope.Inner.Inner2.S(10),
            new Scope.Inner.Inner2.S(20),
        };
        var r3 = await prx.OpSSeqAsync(p3);
        Assert.That(r3, Is.EqualTo(p3));
    }

    [Test]
    public async Task Scope_operations3()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new Operations3())
            .BuildServiceProvider();

        var prx = Scope.Inner.Inner2.OperationsPrx.FromConnection(
            provider.GetRequiredService<Connection>(),
            "/service");

        var p1 = new Scope.Inner.Inner2.S(10);
        var r1 = await prx.OpSAsync(p1);
        Assert.That(r1, Is.EqualTo(p1));

        var p2 = new Dictionary<string, Scope.Inner.Inner2.S>
        {
            ["10"] = new Scope.Inner.Inner2.S(10),
            ["20"] = new Scope.Inner.Inner2.S(20),
        };
        var r2 = await prx.OpSMapAsync(p2);
        Assert.That(r2, Is.EqualTo(p2));

        var p3 = new Scope.Inner.Inner2.S[]
        {
            new Scope.Inner.Inner2.S(10),
            new Scope.Inner.Inner2.S(20),
        };
        var r3 = await prx.OpSSeqAsync(p3);
        Assert.That(r3, Is.EqualTo(p3));
    }
    [Test]
    public async Task Scope_operations4()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new Operations4())
            .BuildServiceProvider();

        var prx = Scope.Inner.Test.Inner2.OperationsPrx.FromConnection(
            provider.GetRequiredService<Connection>(),
            "/service");

        var p1 = new Scope.S(10);
        var r1 = await prx.OpSAsync(p1);
        Assert.That(r1, Is.EqualTo(p1));

        var p2 = new Dictionary<string, Scope.S>
        {
            ["10"] = new Scope.S(10),
            ["20"] = new Scope.S(20),
        };
        var r2 = await prx.OpSMapAsync(p2);
        Assert.That(r2, Is.EqualTo(p2));

        var p3 = new Scope.S[]
        {
            new Scope.S(10),
            new Scope.S(20),
        };
        var r3 = await prx.OpSSeqAsync(p3);
        Assert.That(r3, Is.EqualTo(p3));
    }
}
