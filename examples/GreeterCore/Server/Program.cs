// Copyright (c) ZeroC, Inc.

using GreeterCoreExample;
using IceRpc;

// Create a server that will dispatch all requests to the same dispatcher, an instance of Chatbot.
await using var server = new Server(new Chatbot());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
