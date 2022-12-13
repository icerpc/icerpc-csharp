// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

using var cts = new CancellationTokenSource();

// Add the compressor middleware to the dispatch pipeline.
Router router = new Router().UseCompressor(CompressionFormat.Brotli);
router.Map<IHello>(new Hello());

await using var server = new Server(router);

// Shuts down the server on Ctrl+C
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

server.Listen();
await server.ShutdownComplete;
