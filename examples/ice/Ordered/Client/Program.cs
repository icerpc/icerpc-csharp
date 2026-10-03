// Copyright (c) ZeroC, Inc.

using IceRpc;
using Journal;

// Use the ice protocol. We use the default port for the ice protocol, 4061.
await using var connection = new ClientConnection(new Uri("ice://localhost"));

// The service address URI includes the protocol to use (ice).
var simpleLogger = new SimpleLoggerProxy(connection, new Uri("ice:/logger"));

// Send each log entry in its own oneway request.
//
// A oneway invocation completes as soon as the request is sent, without waiting for the server to dispatch it. The ice
// protocol writes the requests to the connection in order, and the server reads them in the same order; the only
// question is whether the server dispatches them concurrently or one at a time.

Console.Write("Sending log entries with SimpleLogger, one oneway request per entry...");

for (int i = 1; i <= 10; ++i)
{
    await simpleLogger.LogAsync($"Performed step #{i}");
}

Console.WriteLine(" done.");

await connection.ShutdownAsync();
