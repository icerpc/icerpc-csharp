// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

/// <summary>A service that implements Slice interface Hello.</summary>
internal class Hello : Service, IHelloService
{
    internal string Greeting { get; set; } = "Hello";

    public ValueTask<string> SayHelloAsync(IFeatureCollection features, CancellationToken cancellationToken)
    {
        string who = features.Get<ISessionFeature>()?.Name ?? "stranger";
        return new($"{Greeting}, {who}!");
    }
}
