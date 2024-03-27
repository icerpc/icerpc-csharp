// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Protobuf;
using VisitorCenter;

namespace TcpFallbackServer;

/// <summary>A Chatbot is an IceRPC service that implements Protobuf service 'Greeter'.</summary>
[ProtobufService]
internal partial class Chatbot : IGreeterService
{
    public ValueTask<GreetResponse> GreetAsync(
         GreetRequest message,
         IFeatureCollection features,
         CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching Greet request {{ name = '{message.Name}' }}");
        return new(new GreetResponse { Greeting = $"Hello, {message.Name}!" });
    }
}
