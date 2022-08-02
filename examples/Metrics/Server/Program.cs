// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;
using IceRpc.Metrics;

// Add metrics middleware to the router
Router router = new Router().UseMetrics(DispatchEventSource.Log);

var service = new Hello();
router.Map<IHello>(service);

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

// Wait for shutdown
await server.ShutdownComplete;

service.Dispose();
