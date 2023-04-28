// Copyright (c) ZeroC, Inc.

using IceRpc.Features;
using IceRpc.Slice;

namespace GreeterDeadlineExample;

/// <summary>A SloppyChatbot is an IceRPC service that implements Slice interface 'Greeter'.</summary>
/// <remarks>The sloppy chatbot always delays its responses by 1 second.</remarks>
internal class SloppyChatbot : Service, IGreeterService
{
    public async ValueTask<string> GreetAsync(
        string name,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        Console.WriteLine($"Dispatching greet request {{ name = '{name}' }}");
        return $"Hello, {name}!";
    }
}
