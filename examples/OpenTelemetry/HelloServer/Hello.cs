// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{
    private readonly ICrmProxy _crm;

    public Hello(ICrmProxy crm) => _crm = crm;

    public async ValueTask<string> SayHelloAsync(string name, IFeatureCollection features, CancellationToken cancellationToken)
    {
        Console.WriteLine($"{name} says hello!");
        if (await _crm.TryAddCustomerAsync(name, features, cancellationToken))
        {
            return $"Hello, {name}!";
        }
        else
        {
            return $"Welcome back, {name}!";
        }
    }
}
