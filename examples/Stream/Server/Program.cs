// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

using var cancellationSource = new CancellationTokenSource();
await using var server = new Server(new NumberStream(cancellationSource.Token));

// Shuts down the server on Ctrl+C or Ctrl+Break
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
    _ = server.ShutdownAsync();
};

// Start the server
server.Listen();

Console.WriteLine("Server is waiting for connections...");

await server.ShutdownComplete;
