// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using VisitorCenter;
using ZeroC.Slice; // for the Result<TSuccess, TFailure> generic type

namespace CustomErrorServer;

/// <summary>A Chatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
[SliceService]
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
            "" => new GreeterError.EmptyName(),
            "jimmy" => new GreeterError.Away(DateTime.Now + TimeSpan.FromMinutes(5)),
            _ when name.Length > MaxLength => new GreeterError.NameTooLong(MaxLength),
            _ => $"Hello, {name}!"
        };

        return new(result);
    }
}
