// Copyright (c) ZeroC, Inc. All rights reserved.

using MetricsExample;
using IceRpc;

// Add the metrics middleware to the dispatch pipeline.
Router router = new Router().UseMetrics();

router.Map<IHello>(new Hello());

await using var server = new Server(router);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
