// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Hello'.</summary>
internal class Chatbot : Service, IHelloService
{
    internal string Greeting { get; set; } = "Hello";

    public ValueTask<string> SayHelloAsync(IFeatureCollection features, CancellationToken cancellationToken)
    {
        string who = features.Get<IAuthenticationFeature>()?.Name ?? "stranger";
        Console.WriteLine($"Dispatching sayHello request {{ name = '{who}' }}");
        return new($"{Greeting}, {who}!");
    }
}
