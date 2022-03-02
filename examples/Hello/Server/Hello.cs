// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    public ValueTask SayHelloAsync(string name, Dispatch dispatch, CancellationToken cancel)
    {
        Console.WriteLine($"Hello, {name}!");
        return default;
    }
}
