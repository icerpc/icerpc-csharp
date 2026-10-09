// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using Journal;
using System.Security.Cryptography;

namespace OrderedServer;

/// <summary>Implements interface <c>SimpleLogger</c> (defined in SimpleLogger.ice) by printing each message to the
/// console.</summary>
[Service]
internal partial class Scribe : ISimpleLoggerService
{
    public async ValueTask LogAsync(string message, IFeatureCollection features, CancellationToken cancellationToken)
    {
        // This brief random delay simulates the variable processing time of a real logger.
        await Task.Delay(TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(10)), cancellationToken);

        Console.WriteLine($"Scribe: {message}");
    }
}
