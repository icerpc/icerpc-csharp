// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

// Add the request context middleware to the dispatch pipeline.
Router router = new Router().UseRequestContext();
router.Map<IHello>(new Hello());

await using var server = new Server(router);

// Shuts down the server on Ctrl+C
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

await server.ListenAsync();
await server.ShutdownComplete;
