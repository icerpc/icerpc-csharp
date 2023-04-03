// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace GenericHostExample;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
public class Chatbot : Service, IGreeterService
{
    public async ValueTask<string> GreetAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        await Console.Out.WriteLineAsync($"Dispatching greet request {{ name = '{name}' }}");
        return new($"Greeter, {name}!");
    }
}
