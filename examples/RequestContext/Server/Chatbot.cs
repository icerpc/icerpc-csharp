// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace RequestContextExample;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
internal class Chatbot : Service, IGreeterService
{
    public ValueTask<string> GreetAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");

        // The request context sent by the client is available to the dispatch as the IRequestContextFeature.
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
