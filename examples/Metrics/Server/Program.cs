// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

// Add the metrics middleware to the dispatch pipeline.
Router router = new Router().UseMetrics();

router.Map<IHello>(new Hello());

await using var server = new Server(router);

// Shuts down the server on Ctrl+C.
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

server.Listen();

Console.WriteLine("Server is waiting for connections...");

await server.ShutdownComplete;
