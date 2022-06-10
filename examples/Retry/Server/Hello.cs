// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc.Features;
using IceRpc.Slice;
using System.Security.Cryptography;

namespace Demo;

public class Hello : Service, IHello
{
    private readonly string _endpoint;

    public Hello(string endpoint) => _endpoint = endpoint;

    public ValueTask<string> SayHelloAsync(string name, IFeatureCollection features, CancellationToken cancel)
    {
        // 50% failures/success ratio
        if (RandomNumberGenerator.GetInt32(10) > 5)
        {
            throw new DispatchException(IceRpc.RetryPolicy.Immediately);
        }
        else
        {
            Console.WriteLine($"{name} says hello!");
            return new($"Hello, {name}, from server {_endpoint}!");
        }
    }
}
