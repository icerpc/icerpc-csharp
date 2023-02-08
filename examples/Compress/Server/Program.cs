// Copyright (c) ZeroC, Inc.

using CompressExample;
using IceRpc;

// Add the compressor middleware to the dispatch pipeline.
Router router = new Router().UseCompressor(CompressionFormat.Brotli);
router.Map<IHelloService>(new Hello());

await using var server = new Server(router);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
