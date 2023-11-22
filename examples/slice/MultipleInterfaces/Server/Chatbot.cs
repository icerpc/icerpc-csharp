// Copyright (c) ZeroC, Inc.

using Metrics;
using IceRpc.Features;
using IceRpc.Slice;
using VisitorCenter;

namespace MultipleInterfacesServer;

/// <summary>A Chatbot is an IceRPC service that implements the 'Greeter' and 'RequestCounter' Slice interfaces.
/// </summary>
[SliceService]
internal partial class Chatbot : IGreeterService, IRequestCounterService
{
    private int _requestCount;

    public ValueTask<int> GetRequestCountAsync(IFeatureCollection features, CancellationToken cancellationToken) =>
        new(_requestCount);

    public ValueTask<string> GreetAsync(string name, IFeatureCollection features, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching Greet request {{ name = '{name}' }}");
        Interlocked.Increment(ref _requestCount);
        return new($"Hello, {name}!");
    }
}
