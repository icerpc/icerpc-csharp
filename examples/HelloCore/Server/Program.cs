// Copyright (c) ZeroC, Inc.

using HelloCoreExample;
using IceRpc;

await using var server = new Server(new HelloCore());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
