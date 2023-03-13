// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace GenericHostExample;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Hello'.</summary>
public class Chatbot : Service, IHelloService
{
    public async ValueTask<string> SayHelloAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        await Console.Out.WriteLineAsync($"Dispatching sayHello request {{ name = '{name}' }}");
        return new($"Hello, {name}!");
    }
}
