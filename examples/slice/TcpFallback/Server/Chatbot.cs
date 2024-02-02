// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using VisitorCenter;

namespace TcpFallbackServer;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
[SliceService]
internal partial class Chatbot : IGreeterService
{
    public ValueTask<string> GreetAsync(string name, IFeatureCollection features, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");
        return new($"Hello, {name}!");
    }
}
