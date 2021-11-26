// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;

namespace Demo;

public class Hello : Service, IHello
{
    public ValueTask<string?> SayHelloAsync(string? greeting, Dispatch dispatch, CancellationToken cancel)
    {
        Console.Out.WriteLine("Hello World!");
        return new(greeting + ", server!");
    }
}
