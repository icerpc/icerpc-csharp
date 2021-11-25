// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Demo
{
    public class Hello : Service, IHello
    {
        public ValueTask<string?> SayHelloAsync(string? greeting, Dispatch dispatch, CancellationToken cancel)
        {
            Console.Out.WriteLine("Hello World!");
            return new(greeting + ", server!");
        }
    }
}
