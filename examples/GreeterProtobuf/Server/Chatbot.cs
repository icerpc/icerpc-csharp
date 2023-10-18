// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Protobuf;
using VisitorCenter;

namespace GreeterProtobufServer;

/// <summary>A Chatbot is an IceRPC service that implements Protobuf service 'Greeter'.</summary>
[ProtobufService]
internal partial class Chatbot : IGreeterService
{
    public ValueTask<GreetResponse> GreetAsync(GreetRequest message, IFeatureCollection? features, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{message.Name}' }}");
        var response = new GreetResponse();
        response.Greeting = $"Hello, {message.Name}!";
        return new(response);
    }
}
