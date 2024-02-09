// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc.Features;
using IceRpc.Protobuf;
using Metrics;
using VisitorCenter;

namespace MultipleServicesServer;

/// <summary>A Chatbot is an IceRPC service that implements the 'Greeter' and 'RequestCounter' Protobuf services.
/// </summary>
[ProtobufService]
internal partial class Chatbot : IGreeterService, IRequestCounterService
{
    private int _requestCount;

    public ValueTask<GetRequestCountResponse> GetRequestCountAsync(
        Empty message,
        IFeatureCollection features,
        CancellationToken cancellationToken) =>
        new(new GetRequestCountResponse { Count = _requestCount });

    public ValueTask<GreetResponse> GreetAsync(
        GreetRequest message,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching Greet request {{ name = '{message.Name}' }}");
        Interlocked.Increment(ref _requestCount);
        return new(new GreetResponse { Greeting = $"Hello, {message.Name}!" });
    }
}
