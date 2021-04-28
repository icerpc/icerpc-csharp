// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Slice.Test.Escape.@abstract;
using IceRpc.Test;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class Client : TestHelper
{
    public sealed class Case : Icase
    {
        public ValueTask<int> catchAsync(int @checked, IceRpc.Dispatch dispatch, CancellationToken cancel) =>
            new(0);
    }

    public sealed class Decimal : Idecimal
    {
        public ValueTask defaultAsync(IceRpc.Dispatch dispatch, CancellationToken cancel) => default;
    }

    public sealed class Explicit : Iexplicit
    {
        public ValueTask<int> catchAsync(int @checked, IceRpc.Dispatch dispatch, CancellationToken cancel) =>
            new(0);

        public ValueTask defaultAsync(IceRpc.Dispatch dispatch, CancellationToken cancel)
        {
            Assert(dispatch.Operation == "default");
            return default;
        }
    }

    public sealed class Test1I : IceRpc.Slice.Test.Escape.@abstract.System.ITest
    {
        public ValueTask opAsync(IceRpc.Dispatch dispatch, CancellationToken cancel) => default;
    }

    public sealed class Test2I : IceRpc.Slice.Test.Escape.System.ITest
    {
        public ValueTask opAsync(IceRpc.Dispatch dispatch, CancellationToken cancel) => default;
    }

    public static async Task TestTypesAsync()
    {
        @as a = @as.@base;
        Assert(a == @as.@base);
        var b = new @break();
        b.@readonly = 0;
        Assert(b.@readonly == 0);
        var c = new Case();
        Assert(c != null);
        IcasePrx? c1 = null;
        Assert(c1 == null);
        int c2;
        if (c1 != null)
        {
            c2 = await c1.catchAsync(0);
            Assert(c2 == 0);
        }
        var d = new Decimal();
        Assert(d != null);
        IdecimalPrx? d1 = null;
        if (d1 != null)
        {
            await d1.defaultAsync();
        }
        Assert(d1 == null);
        var e = new @delegate();
        Assert(e != null);
        @delegate? e1 = null;
        Assert(e1 == null);
        IexplicitPrx? f1 = null;
        if (f1 != null)
        {
            c2 = await f1.catchAsync(0);
            Assert(c2 == 0);
            await f1.defaultAsync();
        }
        Assert(f1 == null);
        var g2 = new Dictionary<string, @break>();
        Assert(g2 != null);
        var h = new @fixed();
        h.@for = 0;
        Assert(h != null);
        var i = new @foreach();
        i.@for = 0;
        i.@goto = 1;
        i.@if = 2;
        Assert(i != null);
    }

    public override async Task RunAsync(string[] args)
    {
        var router = new Router();
        router.Map("/test", new Decimal());
        router.Map("/test1", new Test1I());
        router.Map("/test2", new Test2I());

        await using var server = new IceRpc.Server
        {
            Communicator = Communicator,
            Dispatcher = router,
            Endpoint = $"ice+coloc://{Guid.NewGuid()}"
        };

        server.Listen();

        Output.Write("testing operation name... ");
        Output.Flush();
        IdecimalPrx p = server.CreateProxy<IdecimalPrx>("/test");
        await p.defaultAsync();
        Output.WriteLine("ok");

        Output.Write("testing System as module name... ");
        Output.Flush();
        IceRpc.Slice.Test.Escape.@abstract.System.ITestPrx t1 =
            server.CreateProxy<IceRpc.Slice.Test.Escape.@abstract.System.ITestPrx>("/test1");
        await t1.opAsync();

        IceRpc.Slice.Test.Escape.System.ITestPrx t2 =
            server.CreateProxy<IceRpc.Slice.Test.Escape.System.ITestPrx>("/test2");
        await t2.opAsync();
        Output.WriteLine("ok");

        Output.Write("testing types... ");
        Output.Flush();
        await TestTypesAsync();
        Output.WriteLine("ok");
    }

    public static async Task<int> Main(string[] args)
    {
        await using var communicator = CreateCommunicator(ref args);
        return await RunTestAsync<Client>(communicator, args);
    }
}
