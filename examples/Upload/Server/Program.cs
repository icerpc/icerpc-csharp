// Copyright (c) ZeroC, Inc. All rights reserved.

using Demo;
using IceRpc;

await using var server = new Server(new Uploader());

// Shuts down the server on Ctrl+C.
Console.CancelKeyPress += (sender, eventArgs) =>
{
    eventArgs.Cancel = true;
    _ = server.ShutdownAsync();
};

Console.WriteLine("Server is waiting for connections...");
server.Listen();

await server.ShutdownComplete;
