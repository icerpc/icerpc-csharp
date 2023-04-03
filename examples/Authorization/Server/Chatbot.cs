// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
internal class Chatbot : Service, IGreeterService
{
    internal string Greeting { get; set; } = "Greeter";

    public ValueTask<string> GreetAsync(IFeatureCollection features, CancellationToken cancellationToken)
    {
        string who = features.Get<ISessionFeature>()?.Name ?? "stranger";
        Console.WriteLine($"Dispatching greet request {{ name = '{who}' }}");
        return new($"{Greeting}, {who}!");
    }
}
