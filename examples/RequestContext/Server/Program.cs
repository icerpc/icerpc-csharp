// Copyright (c) ZeroC, Inc.

using IceRpc;
using RequestContextExample;

// Add the request context middleware to the dispatch pipeline.
Router router = new Router().UseRequestContext();
router.Map<IHello>(new Hello());

await using var server = new Server(router);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
