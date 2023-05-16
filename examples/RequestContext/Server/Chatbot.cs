// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using VisitorCenter;

namespace RequestContextServer;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
internal class Chatbot : Service, IGreeterService
{
    public ValueTask<string> GreetAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");

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

        return new($"Hello, {name}!");
    }
}
