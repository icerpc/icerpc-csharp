// Copyright (c) ZeroC, Inc. All rights reserved.

using HelloCoreExample;
using IceRpc;

await using var server = new Server(new HelloCore());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
