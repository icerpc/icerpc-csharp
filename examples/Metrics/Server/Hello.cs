// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    public ValueTask SayHelloAsync(IFeatureCollection features, CancellationToken cancel)
    {
        Console.WriteLine("Received a hello request at " + DateTime.Now);
        return default;
    }
}
