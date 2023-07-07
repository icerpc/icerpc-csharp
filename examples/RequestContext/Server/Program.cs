// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Slice;
using RequestContextServer;
using VisitorCenter;

// Add the request context middleware to the dispatch pipeline.
Router router = new Router().UseRequestContext();
router.Map<IGreeterService>(new Chatbot());

await using var server = new Server(router);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
