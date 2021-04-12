// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using IceRpc;
using IceRpc.Slice.Test.Escape.@abstract;
using IceRpc.Test;

public class Client : TestHelper
{
    public sealed class Case : Icase
    {
        public ValueTask<int> catchAsync(int @checked, IceRpc.Current current, CancellationToken cancel) =>
            new(0);
    }

    public sealed class Decimal : Idecimal
    {
        public void @default(IceRpc.Current current, CancellationToken cancel)
        {
        }
    }

    public sealed class Explicit : Iexplicit
    {
        public ValueTask<int> catchAsync(int @checked, IceRpc.Current current, CancellationToken cancel) =>
            new(0);

        public void @default(IceRpc.Current current, CancellationToken cancel) => Assert(current.Operation == "default");
    }

    public sealed class Test1I : IceRpc.Slice.Test.Escape.@abstract.System.ITest
    {
        public void op(IceRpc.Current current, CancellationToken cancel)
        {
        }
    }

    public sealed class Test2I : IceRpc.Slice.Test.Escape.System.ITest
    {
        public void op(IceRpc.Current current, CancellationToken cancel)
        {
        }
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
        await using var server = new IceRpc.Server { Communicator = Communicator };
        server.Add("/test", new Decimal());
        server.Add("/test1", new Test1I());
        server.Add("/test2", new Test2I());
        _ = server.ListenAndServeAsync();

        Output.Write("testing operation name... ");
        Output.Flush();
        IdecimalPrx p = IdecimalPrx.Factory.Create(server, "/test");
        p.@default();
        Output.WriteLine("ok");

        Output.Write("testing System as module name... ");
        Output.Flush();
        IceRpc.Slice.Test.Escape.@abstract.System.ITestPrx t1 =
            IceRpc.Slice.Test.Escape.@abstract.System.ITestPrx.Factory.Create(server, "/test1");
        t1.op();

        IceRpc.Slice.Test.Escape.System.ITestPrx t2 =
            IceRpc.Slice.Test.Escape.System.ITestPrx.Factory.Create(server, "/test2");
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
