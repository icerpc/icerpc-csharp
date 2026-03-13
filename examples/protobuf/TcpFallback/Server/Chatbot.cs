// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using VisitorCenter;

namespace TcpFallbackServer;

/// <summary>A Chatbot is an IceRPC service that implements Protobuf service 'Greeter'.</summary>
[Service]
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
