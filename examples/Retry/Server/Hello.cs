// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
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
            // Aleatory set the retry policy to RetryPolicy.Immediately or RetryPolicy.OtherReplica
            throw new DispatchException(
                DispatchErrorCode.UnhandledException,
                RandomNumberGenerator.GetInt32(10) > 5 ? RetryPolicy.Immediately : RetryPolicy.OtherReplica);
        }
        else
        {
            Console.WriteLine($"{name} says hello!");
            return new($"Hello, {name}, from server {_endpoint}!");
        }
    }
}
