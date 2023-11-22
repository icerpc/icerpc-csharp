// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Protobuf;
using VisitorCenter;

namespace DeadlineServer;

/// <summary>A SlowChatbot is an IceRPC service that implements Protobuf service 'Greeter'.</summary>
/// <remarks>The slow chatbot always delays its responses by 1 second.</remarks>
[ProtobufService]
internal partial class SlowChatbot : IGreeterService
{
    public async ValueTask<GreetResponse> GreetAsync(
        GreetRequest message,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching Greet request {{ name = '{message.Name}' }}");
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            Console.WriteLine($"Dispatch canceled: {exception.Message}");
            throw;
        }
        return new GreetResponse { Greeting = $"Hello, {message.Name}!" };
    }
}
