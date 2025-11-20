// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Protobuf;
using VisitorCenter;

namespace GenericHostServer;

/// <summary>A Chatbot is an IceRPC service that implements Protobuf service 'Greeter'.</summary>
[ProtobufService]
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "This class is instantiated dynamically by the dependency injection container.")]
internal partial class Chatbot : IGreeterService
{
    /// <inheritdoc/>
    public ValueTask<GreetResponse> GreetAsync(
        GreetRequest message,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching Greet request {{ name = '{message.Name}' }}");
        return new(new GreetResponse { Greeting = $"Hello, {message.Name}!" });
    }
}
