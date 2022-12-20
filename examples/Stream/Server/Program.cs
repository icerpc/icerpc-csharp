// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using StreamExample;

using var cts = new CancellationTokenSource();
await using var server = new Server(new NumberStream());

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
