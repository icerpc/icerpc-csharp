// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

using var cts = new CancellationTokenSource();
await using var server = new Server(new NumberStream());

// Shuts down the server on Ctrl+C.
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync(new CancellationToken(canceled: true));
};

server.Listen();

Console.WriteLine("Server is waiting for connections...");

await server.ShutdownComplete;
