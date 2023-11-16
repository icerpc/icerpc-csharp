// Copyright (c) ZeroC, Inc.

using CompressServer;
using IceRpc;
using IceRpc.Slice;
using VisitorCenter;

// Add the Compressor middleware to the dispatch pipeline.
Router router = new Router().UseCompressor(CompressionFormat.Brotli);
router.Map<IGreeterService>(new Chatbot());

await using var server = new Server(router);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
