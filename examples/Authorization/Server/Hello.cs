// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace AuthorizationExample;

public class HelloService : Service, IHello
{
    public string Greeting { get; set; } = "Hello";

    public ValueTask<string> SayHelloAsync(IFeatureCollection features, CancellationToken cancellationToken)
    {
        string who;
        if (features.Get<ISessionFeature>() is ISessionFeature sessionFeature)
        {
            who = sessionFeature.Name;
        }
        else
        {
            who = "stranger";
        }

        return new($"{Greeting}, {who}!");
    }
}
