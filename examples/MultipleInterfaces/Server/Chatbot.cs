// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace MultipleInterfacesExample;

/// <summary>A Chatbot is an IceRPC service that implements 'Hello' and 'RequestCounter' Slice interfaces.</summary>
internal class Chatbot : Service, IHelloService, IRequestCounterService
{
    private int _requestCount;

    public ValueTask<int> GetRequestCountAsync(
        IFeatureCollection features,
        CancellationToken cancellationToken) => new(_requestCount);

    public ValueTask<string> SayHelloAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching sayHello request {{ name = '{name}' }}");
        Interlocked.Increment(ref _requestCount);
        return new($"Hello, {name}!");
    }
}
