// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace HelloQuicExample;

internal class Chatbot : Service, IHelloService
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