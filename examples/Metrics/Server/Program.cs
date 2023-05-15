// Copyright (c) ZeroC, Inc.

using IceRpc;
using MetricsServer;
using VisitorCenter;

// Add the metrics middleware to the dispatch pipeline.
Router router = new Router().UseMetrics();

router.Map<IGreeterService>(new Chatbot());

await using var server = new Server(router);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
