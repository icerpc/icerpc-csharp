// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using Journal;
using System.Security.Cryptography;

namespace OrderedServer;

/// <summary>Implements Slice interface <c>SimpleLogger</c> by printing each message to the console.</summary>
[Service]
internal partial class Scribe : ISimpleLoggerService
{
    public async ValueTask LogAsync(string message, IFeatureCollection features, CancellationToken cancellationToken)
    {
        // Each log request is dispatched concurrently with the others. This brief random delay simulates the variable
        // processing time of a real logger, and makes it more likely that the entries are printed out of order.
        await Task.Delay(TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(10)), cancellationToken);

        Console.WriteLine($"Scribe: {message}");
    }
}
