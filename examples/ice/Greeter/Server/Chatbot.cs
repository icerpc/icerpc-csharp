// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using VisitorCenter;

namespace GreeterServer;

/// <summary>A Chatbot is a service that implements interface 'Greeter' (defined in Greeter.ice).</summary>
[Service]
internal partial class Chatbot : IGreeterService
{
    public ValueTask<string> GreetAsync(string name, IFeatureCollection features, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");
        return new($"Hello, {name}!");
    }
}
