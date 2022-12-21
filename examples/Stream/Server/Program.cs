// Copyright (c) ZeroC, Inc. All rights reserved.

using IceRpc;
using StreamExample;

using var cts = new CancellationTokenSource();
await using var server = new Server(new NumberStream());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
