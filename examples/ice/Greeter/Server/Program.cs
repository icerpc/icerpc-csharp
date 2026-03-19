// Copyright (c) ZeroC, Inc.

using IceRpc;
using GreeterServer;

// Use the ice protocol for compatibility with ZeroC Ice.
await using var server = new Server(new Chatbot(), new Uri("ice://[::0]"));

// Start accepting requests on the default port for the ice protocol, 4061.
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
