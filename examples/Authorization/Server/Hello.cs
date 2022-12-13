// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

/// <summary>The implementation of the IHello service.</summary>
public class Hello : Service, IHello
{
    public string Greeting { get; internal set; } = "Hello";

    public ValueTask<string> SayHelloAsync(IFeatureCollection features, CancellationToken cancellationToken)
    {
        string who = features.Get<ISessionFeature>()?.Name ?? "stranger";
        return new($"{Greeting}, {who}!");
    }
}
