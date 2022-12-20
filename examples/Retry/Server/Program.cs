// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using RetryExample;

if (args.Length < 1)
{
    Console.WriteLine("Missing server number argument");
    return;
}

int number;
if (!int.TryParse(args[0], out number))
{
    Console.WriteLine($"Invalid server number argument '{args[0]}', expected a number");
    return;
}

var serverAddress = new ServerAddress(new Uri($"icerpc://127.0.0.1:{10000 + number}/"));

await using var server = new Server(new Hello(number), serverAddress);

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
