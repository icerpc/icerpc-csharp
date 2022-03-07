// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    public async ValueTask<string> SayHelloAsync(string name, Dispatch dispatch, CancellationToken cancel)
    {
        await Console.Out.WriteLineAsync($"{name} says hello!");
        return new($"Hello, {name}!");
    }
}
