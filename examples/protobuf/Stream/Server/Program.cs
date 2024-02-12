// Copyright (c) ZeroC, Inc.

using IceRpc;
using StreamServer;

await using var server = new Server(new RandomGenerator());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
