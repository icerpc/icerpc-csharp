// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc;
using IceRpc.Features;
using Journal;

namespace OnewayServer;

/// <summary>A Scribe is an IceRPC service that implements Protobuf service <c>SimpleLogger</c> by printing each entry
/// to the console.</summary>
[Service]
internal partial class Scribe : ISimpleLoggerService
{
    public async ValueTask<Empty> LogAsync(
        LogEntry message,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        // This delay simulates the processing time of a real logger.
        await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);

        Console.WriteLine($"Scribe: [{message.Timestamp.ToDateTime().ToLocalTime():HH:mm:ss.fff}] {message.Message}");
        return new Empty();
    }
}
