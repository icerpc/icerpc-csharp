// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    public ValueTask<string> SayHelloAsync(
        string name, 
        IFeatureCollection features, 
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"{name} says hello!");
        return new($"Hello, {name}!");
    }
}
