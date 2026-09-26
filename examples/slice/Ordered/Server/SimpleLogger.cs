// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using OrderedExample;
using System.Security.Cryptography;

namespace OrderedServer;

/// <summary>Implements Slice interface `SimpleLogger` by printing each log entry to the console.</summary>
[Service]
internal partial class SimpleLogger : ISimpleLoggerService
{
    public async ValueTask LogAsync(
        uint serial,
        DateTime timeStamp,
        string message,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        // Each log request is dispatched concurrently with the others. This brief random delay makes it more likely
        // that the entries are printed out of order.
        await Task.Delay(TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(10)), cancellationToken);

        Console.WriteLine($"[{timeStamp.ToLocalTime():HH:mm:ss.fff}] SimpleLogger: #{serial} {message}");
    }
}
