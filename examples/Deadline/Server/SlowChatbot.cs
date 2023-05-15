// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;
using VisitorCenter;

namespace DeadlineServer;

/// <summary>A SlowChatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
/// <remarks>The slow chatbot always delays its responses by 1 second.</remarks>
internal class SlowChatbot : Service, IGreeterService
{
    public async ValueTask<string> GreetAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            Console.WriteLine($"Dispatch canceled: {exception.Message}");
            throw;
        }
        return $"Hello, {name}!";
    }
}
