// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using IceRpc.Protobuf;
using System.Security.Cryptography;
using VisitorCenter;

namespace RetryServer;

/// <summary>A Chatbot is an IceRPC service that implements Protobuf service 'Greeter'.</summary>
[ProtobufService]
internal partial class Chatbot : IGreeterService
{
    private readonly int _serverNumber;

    internal Chatbot(int serverNumber) => _serverNumber = serverNumber;

    public ValueTask<GreetResponse> GreetAsync(
        GreetRequest message,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        // 50% failure/success ratio
        if (RandomNumberGenerator.GetInt32(10) < 5)
        {
            Console.WriteLine(
                $"Dispatching Greet request {{ name = '{message.name}' }} => DispatchException(StatusCode.Unavailable)");
            throw new DispatchException(StatusCode.Unavailable);
        }
        else
        {
            Console.WriteLine($"Dispatching Greet request {{ name = '{message.name}' }} => greeting");
            return new(new GreetResponse { Greeting = $"Hello, {message.Name}, from server #{_serverNumber}!" });
        }
    }
}
