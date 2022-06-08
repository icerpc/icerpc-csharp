// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    public ValueTask<string> SayHelloAsync(string name, IFeatureCollection features, CancellationToken cancel)
    {
        Console.WriteLine($"{name} says hello!");
        return new($"Hello, {name}!");
    }
}

public class Forwarder : Service, IHello
{
    private IHelloPrx _hello;

    public Forwarder(IHelloPrx hello) => _hello = hello;

    public async ValueTask<string> SayHelloAsync(string name, IFeatureCollection features, CancellationToken cancel) =>
        await _hello.SayHelloAsync(name, features, cancel);
}
