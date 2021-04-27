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
    public sealed class Case : IAsynccase
    {
        public ValueTask<int> catchAsync(int @checked, IceRpc.Dispatch dispatch, CancellationToken cancel) =>
            new(0);
    }

    public sealed class Decimal : IAsyncdecimal
    {
        public ValueTask defaultAsync(IceRpc.Dispatch dispatch, CancellationToken cancel) => default;
    }

    public sealed class Explicit : IAsyncexplicit
    {
        public ValueTask<int> catchAsync(int @checked, IceRpc.Dispatch dispatch, CancellationToken cancel) =>
            new(0);

        public ValueTask defaultAsync(IceRpc.Dispatch dispatch, CancellationToken cancel)
        {
            Assert(dispatch.Operation == "default");
            return default;
        }
    }

    public sealed class Test1I : IceRpc.Slice.Test.Escape.@abstract.System.IAsyncTest
    {
        public ValueTask opAsync(IceRpc.Dispatch dispatch, CancellationToken cancel) => default;
    }

    public sealed class Test2I : IceRpc.Slice.Test.Escape.System.IAsyncTest
    {
        public ValueTask opAsync(IceRpc.Dispatch dispatch, CancellationToken cancel) => default;
    }

    public static void TestTypes()
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
            c2 = c1.@catch(0);
            Assert(c2 == 0);
        }
        var d = new Decimal();
        Assert(d != null);
        IdecimalPrx? d1 = null;
        if (d1 != null)
        {
            d1.@default();
        }
        Assert(d1 == null);
        var e = new @delegate();
        Assert(e != null);
        @delegate? e1 = null;
        Assert(e1 == null);
        IexplicitPrx? f1 = null;
        if (f1 != null)
        {
            c2 = f1.@catch(0);
            Assert(c2 == 0);
            f1.@default();
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
        p.@default();
        Output.WriteLine("ok");

        Output.Write("testing System as module name... ");
        Output.Flush();
        IceRpc.Slice.Test.Escape.@abstract.System.ITestPrx t1 =
            server.CreateProxy<IceRpc.Slice.Test.Escape.@abstract.System.ITestPrx>("/test1");
        t1.op();

        IceRpc.Slice.Test.Escape.System.ITestPrx t2 =
            server.CreateProxy<IceRpc.Slice.Test.Escape.System.ITestPrx>("/test2");
        t2.op();
        Output.WriteLine("ok");

        Output.Write("testing types... ");
        Output.Flush();
        TestTypes();
        Output.WriteLine("ok");
    }

    public static async Task<int> Main(string[] args)
    {
        await using var communicator = CreateCommunicator(ref args);
        return await RunTestAsync<Client>(communicator, args);
    }
}
