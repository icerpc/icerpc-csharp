// Copyright (c) ZeroC, Inc.

using IceRpc;
using MinimalServer;

// Use the ice protocol for compatibility with ZeroC Ice.
await using var server = new Server(new HelloService(), new Uri("ice://[::0]:10000"));
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
