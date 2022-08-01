// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Metrics;

using var cancellationSource = new CancellationTokenSource();

// Adding metrics middleware to the router
using var eventSource = DispatchEventSource.Log;
Router router = new Router().UseMetrics(eventSource);
router.Map<IHello>(new Hello());

await using var server = new Server(router);

// Shuts down the server on Ctrl+C or Ctrl+Break
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

// Start the server
server.Listen();

Console.WriteLine("Server is waiting for connections...");

await server.ShutdownComplete;
