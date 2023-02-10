// Copyright (c) ZeroC, Inc.

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

await using var server = new Server(new Chatbot(number), serverAddress);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
