// Copyright (c) ZeroC, Inc.

using HelloCoreExample;
using IceRpc;

// Create a server that will dispatch all requests to the same dispatcher/service, an instance of Chatbot.
await using var server = new Server(new Chatbot());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
