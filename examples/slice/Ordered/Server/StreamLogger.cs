// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Features;
using OrderedExample;

namespace OrderedServer;

/// <summary>Implements Slice interface `StreamLogger` by printing each streamed log entry to the console.</summary>
[Service]
internal partial class StreamLogger : IStreamLoggerService
{
    public async ValueTask LogAsync(
        IAsyncStream<LogEntry> logEntries,
        IFeatureCollection features,
        CancellationToken cancellationToken)
    {
        // This method owns logEntries and its underlying transport stream.
        using IAsyncStream<LogEntry> _ = logEntries;

        // The entries arrive in the order the client wrote them.
        await foreach (LogEntry entry in logEntries.WithCancellation(cancellationToken))
        {
            Console.WriteLine($"[{entry.TimeStamp.ToLocalTime():HH:mm:ss.fff}] StreamLogger: {entry.Message}");
        }
    }
}
