// Copyright (c) ZeroC, Inc.

using DownloadServer;
using IceRpc;

await using var server = new Server(new EarthImageServer());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
