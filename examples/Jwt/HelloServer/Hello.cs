// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Jwt;
using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    public ValueTask<string> SayHelloAsync(IFeatureCollection features, CancellationToken cancel)
    {
        if (features.Get<IJwtFeature>() is JwtFeature feature)
        {
            Console.WriteLine($"{feature.Token.Subject} says hello!");
            return new($"Hello, {feature.Token.Subject}!");
        }
        else
        {
            throw new DispatchException($"access denied missing Jwt security token", DispatchErrorCode.UnhandledException);
        }
    }
}
