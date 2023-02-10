// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.RequestContext;
using IceRpc.Slice;

namespace RequestContextExample;

internal class Hello : Service, IHelloService
{
    public ValueTask<string> SayHelloAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"{name} says hello!");
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
