// Copyright (c) ZeroC, Inc. All rights reserved.

using System;
using System.Threading;
using IceRpc;

namespace Demo
{
    public class Hello : IHello
    {
        public string? SayHello(string? greeting, Current current, CancellationToken cancel)
        {
            Thread.Sleep(10000);
            Console.Out.WriteLine("Hello World!");
            return greeting + ", server!";
        }
    }
}
