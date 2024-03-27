// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Protobuf;
using VisitorCenter;

namespace RequestContextServer;

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
        // The request context middleware decoded the request context field sent by the client (as an
        // IRequestContextFeature) and inserted this feature in features.
        if (features.Get<IRequestContextFeature>() is IRequestContextFeature contextFeature)
        {
            Console.WriteLine("with RequestContext:");
            foreach ((string key, string value) in contextFeature.Value)
            {
                Console.WriteLine($"  {key}: {value}");
            }
        }
        return new(new GreetResponse { Greeting = $"Hello, {message.Name}!" });
    }
}
