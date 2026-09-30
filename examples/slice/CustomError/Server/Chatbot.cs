// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using VisitorCenter;
using ZeroC.Slice; // for the Result<TSuccess, TFailure> generic type

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

        // A GreeterError variant converts implicitly to GreeterError and a GreeterError converts implicitly to Result,
        // but C# does not chain these two conversions.
        Result<string, GreeterError> result = name switch
        {
            "" => (GreeterError)new GreeterError.EmptyName(),
            "jimmy" => (GreeterError)new GreeterError.Away(DateTime.Now + TimeSpan.FromMinutes(5)),
            _ when name.Length > MaxLength => (GreeterError)new GreeterError.NameTooLong(MaxLength),
            _ => $"Hello, {name}!"
        };

        return new(result);
    }
}
