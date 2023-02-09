// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;
using System.Security.Cryptography;

namespace RetryExample;

internal class Hello : Service, IHelloService
{
    private readonly int _serverNumber;

    internal Hello(int serverNumber) => _serverNumber = serverNumber;

    public ValueTask<string> SayHelloAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        // 50% failure/success ratio
        if (RandomNumberGenerator.GetInt32(10) < 5)
        {
            // Randomly set status code to Unavailable
            throw new DispatchException(
                RandomNumberGenerator.GetInt32(10) < 5 ? StatusCode.Unavailable : StatusCode.ServiceNotFound);
        }
        else
        {
            Console.WriteLine($"{name} says hello!");
            return new($"Hello, {name}, from server #{_serverNumber}!");
        }
    }
}
