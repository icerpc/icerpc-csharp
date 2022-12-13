// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

await using var server = new Server(new Downloader());

// Shut down the server on Ctrl+C
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

server.Listen();
await server.ShutdownComplete;
