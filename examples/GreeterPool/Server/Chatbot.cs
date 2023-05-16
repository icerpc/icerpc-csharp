// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using VisitorCenter;

namespace GreeterServer;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
internal class EnglishChatbot : Service, IGreeterService
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

internal class FrenchChatbot : Service, IGreeterService
{
    public ValueTask<string> GreetAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");
        return new($"Bonjour {name}, comment vas-tu ?");
    }
}

internal class SpanishChatbot : Service, IGreeterService
{
    public ValueTask<string> GreetAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");
        return new($"¡Hola, {name}!");
    }
}
