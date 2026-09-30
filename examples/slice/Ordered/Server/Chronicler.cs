// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using Journal;
using System.Security.Cryptography;

namespace OrderedServer;

/// <summary>Implements Slice interface <c>StreamLogger</c> by printing each streamed message to the console.</summary>
[Service]
internal partial class Chronicler : IStreamLoggerService
{
    public async ValueTask LogAsync(
        IAsyncStream<string> messages,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        // This method owns messages and its underlying transport stream, so it disposes messages when done.
        using IAsyncStream<string> _ = messages;

        // The messages arrive in the order the client wrote them. The same random delay as in Scribe doesn't change
        // this order.
        await foreach (string message in messages.WithCancellation(cancellationToken))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(10)), cancellationToken);

            Console.WriteLine($"Chronicler: {message}");
        }
    }
}
