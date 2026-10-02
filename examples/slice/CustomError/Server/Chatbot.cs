// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using VisitorCenter;
using ZeroC.Slice; // for Result, Success and Failure

namespace CustomErrorServer;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
[Service]
internal partial class Chatbot : IGreeterService
{
    private const int MaxLength = 7;

    public ValueTask<Result<string, GreeterError>> GreetAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");

        Result<string, GreeterError> result = name switch
        {
            "" => new Failure<GreeterError>(new GreeterError.EmptyName()),
            "jimmy" => new Failure<GreeterError>(new GreeterError.Away(DateTime.Now + TimeSpan.FromMinutes(5))),
            _ when name.Length > MaxLength => new Failure<GreeterError>(new GreeterError.NameTooLong(MaxLength)),
            _ => new Success($"Hello, {name}!")
        };

        return new(result);
    }
}
