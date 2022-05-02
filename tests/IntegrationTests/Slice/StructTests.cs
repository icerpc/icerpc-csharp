// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;
using IceRpc.Tests;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace IceRpc.IntegrationTests;

public class StructTests
{
    class StructOperations : Service, IStructOperations
    {
        public ValueTask<MyStructA> Op1Async(MyStructA p1, Dispatch dispatch, CancellationToken cancel) => new(p1);
        public ValueTask<MyStructWithOptionalMembersA> Op2Async(
            MyStructWithOptionalMembersA p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);
        public ValueTask<MyStructWithTaggedMembersA> Op3Async(
            MyStructWithTaggedMembersA p1,
            Dispatch dispatch,
            CancellationToken cancel) => new(p1);
    }

    [Test]
    public async Task Struct_operations()
    {
        await using var provider = new IntegrationTestServiceCollection()
            .UseDispatcher(new StructOperations())
            .BuildServiceProvider();

        var prx = StructOperationsPrx.FromConnection(provider.GetRequiredService<Connection>(), "/service");

        var p1 = new MyStructA
        {
            A = true,
            B = 0,
            C = 1,
            D = 2,
            E = 3,
            F = 4,
            G = 5,
            H = 6,
            I = 7,
            J = 8,
            K = 9,
            L = 10,
            M = 11,
            N = 12,
            O = 13,
            P = "hello"
        };
        MyStructA r1 = await prx.Op1Async(p1);
        Assert.That(r1, Is.EqualTo(p1));

        var p2 = new MyStructWithOptionalMembersA();
        p2.A = true;
        p2.C = 255;
        p2.E = 1024;
        var r2 = await prx.Op2Async(p2);
        Assert.That(r2, Is.EqualTo(p2));

        var p3 = new MyStructWithTaggedMembersA();
        p2.A = true;
        p2.C = 255;
        p2.E = 1024;
        var r3 = await prx.Op3Async(p3);
        Assert.That(r3, Is.EqualTo(p3));
    }
}
