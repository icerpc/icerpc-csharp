// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace MultipleInterfacesExample;

/// <summary>A Chatbot is an IceRPC service that implements the 'Greeter' and 'RequestCounter' Slice interfaces.
/// </summary>
internal class Chatbot : Service, IGreeterService
{
    private int _requestCount;

    public ValueTask<int> GetRequestCountAsync(
        IFeatureCollection features,
        CancellationToken cancellationToken) => new(_requestCount);

    public ValueTask<string> GreetAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");
        Interlocked.Increment(ref _requestCount);
        return new($"Greeter, {name}!");
    }
}
