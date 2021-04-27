// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using System;
using System.Threading;

namespace Demo
{
    public class Hello : IHello
    {
        public string? SayHello(string? greeting, Dispatch dispatch, CancellationToken cancel)
        {
            Console.Out.WriteLine("Hello World!");
            return greeting + ", server!";
        }
    }
}
