// Copyright (c) ZeroC, Inc.

using GreeterDeadlineExample;
using IceRpc;

// Create a server that will dispatch all requests to the same service, an instance of SlowChatbot.
await using var server = new Server(new SlowChatbot());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
