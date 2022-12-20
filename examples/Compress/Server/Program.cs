// Copyright (c) ZeroC, Inc. All rights reserved.

using CompressExample;
using IceRpc;

// Add the compressor middleware to the dispatch pipeline.
Router router = new Router().UseCompressor(CompressionFormat.Brotli);
router.Map<IHello>(new Hello());

await using var server = new Server(router);

// Create a task completion source to keep running until Ctrl+C is pressed.
var cancelKeyPressed = new TaskCompletionSource();
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = cancelKeyPressed.TrySetResult();
};

server.Listen();
await cancelKeyPressed.Task;
await server.ShutdownAsync();
