// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;
using System.Security.Cryptography;

namespace Demo;

public class Hello : Service, IHello
{
    private readonly int _serverNumber;

    public Hello(int serverNumber) => _serverNumber = serverNumber;

    public ValueTask<string> SayHelloAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        // 50% failure/success ratio
        if (RandomNumberGenerator.GetInt32(10) < 5)
        {
            // Randomly set the retry policy to RetryPolicy.Immediately or RetryPolicy.OtherReplica
            throw new DispatchException(
                StatusCode.UnhandledException,
                RandomNumberGenerator.GetInt32(10) < 5 ? RetryPolicy.Immediately : RetryPolicy.OtherReplica);
        }
        else
        {
            Console.WriteLine($"{name} says hello!");
            return new($"Hello, {name}, from server #{_serverNumber}!");
        }
    }
}
