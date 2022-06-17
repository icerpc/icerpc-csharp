// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Jwt;
using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    public ValueTask<string> SayHelloAsync(string name, IFeatureCollection features, CancellationToken cancel)
    {
        if (features.Get<IJwtFeature>() is JwtFeature feature)
        {
            if (feature.Value.Subject != name)
            {
                throw new DispatchException($"access denied: {name} doesn't match sub: {feature.Value.Subject}", DispatchErrorCode.UnhandledException);
            }
            Console.WriteLine($"{name} says hello!");
            return new($"Hello, {name}!");
        }
        else
        {
            throw new DispatchException($"access denied missing Jwt security token: {name}", DispatchErrorCode.UnhandledException);
        }
    }
}
