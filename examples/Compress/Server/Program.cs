// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

using var cancellationSource = new CancellationTokenSource();

// Adding decompression middleware to the router
Router router = new Router().UseDeflate();
router.Map<INumberStream>(new NumberStream());

await using var server = new Server(router);

// Shuts down the server on Ctrl+C or Ctrl+Break
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync(new CancellationToken(canceled: true));
};

// Start the server
server.Listen();

Console.WriteLine("Server is waiting for connections...");

await server.ShutdownComplete;
