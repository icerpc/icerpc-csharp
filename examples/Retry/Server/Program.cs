// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

if (args.Length < 1)
{
    Console.WriteLine("Missing endpoint argument");
    return;
}

await using var server = new Server(
    new ServerOptions
    {
        Endpoint = args[0],
        ConnectionOptions = new ConnectionOptions
        {
            Dispatcher = new Hello(args[0])
        }
    });

// Destroy the server on Ctrl+C or Ctrl+Break
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

server.Listen();
await server.ShutdownComplete;
