// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace CompressExample;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
internal class Chatbot : Service, IGreeterService
{
    public ValueTask<string> GreetAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");
        return new($"Hello, {name}!");
    }
}
