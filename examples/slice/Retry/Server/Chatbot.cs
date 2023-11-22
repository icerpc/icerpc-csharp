// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using IceRpc.Slice;
using System.Security.Cryptography;
using VisitorCenter;

namespace RetryServer;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
[SliceService]
internal partial class Chatbot : IGreeterService
{
    private readonly int _serverNumber;

    internal Chatbot(int serverNumber) => _serverNumber = serverNumber;

    public ValueTask<string> GreetAsync(string name, IFeatureCollection features, CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");
        // 50% failure/success ratio
        if (RandomNumberGenerator.GetInt32(10) < 5)
        {
            throw new DispatchException(StatusCode.Unavailable);
        }
        else
        {
            return new($"Hello, {name}, from server #{_serverNumber}!");
        }
    }
}
