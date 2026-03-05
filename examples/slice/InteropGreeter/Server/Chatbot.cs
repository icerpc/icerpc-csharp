// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Ice;
using IceRpc.Slice;
using VisitorCenter;

namespace InteropGreeterServer;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
// We implement IIceObjectService as well but without actually implementing any method. This allows clients to call
// "checked cast" on the proxy.
[SliceService]
internal partial class Chatbot : IGreeterService, IIceObjectService
{
    public ValueTask<string> GreetAsync(string name, IFeatureCollection features, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");
        return new($"Hello, {name}!");
    }
}
