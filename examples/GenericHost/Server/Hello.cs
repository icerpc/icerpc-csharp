// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    public ValueTask<string> SayHelloAsync(string name, Dispatch dispatch, CancellationToken cancel)
    {
        Console.Out.WriteLineAsync($"{name} says hello!");
        return new($"Hello, {name}!");
    }
}
