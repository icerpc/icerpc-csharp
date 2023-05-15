// Copyright (c) ZeroC, Inc.

using IceRpc;
using UploadServer;

await using var server = new Server(new EarthImageStore());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
