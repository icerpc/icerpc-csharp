// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;

namespace Demo;

public class Hello : Service, IHello
{

    ICustomerListPrx _customerList;

    public Hello(ICustomerListPrx customerList) => _customerList = customerList;

    public async ValueTask<string> SayHelloAsync(string name, IFeatureCollection features, CancellationToken cancel)
    {
        Console.WriteLine($"{name} says hello!");
        if (await _customerList.TryAddCustomerAsync(name, features, cancel))
        {
            return $"Hello, {name}!";
        }
        else
        {
            return $"Welcome back, {name}!";
        }
    }
}
