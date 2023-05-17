// Copyright (c) ZeroC, Inc.

using GreeterPoolServer;
using IceRpc;

Router router = new Router()
    .Map("/greeter/english", new EnglishChatbot())
    .Map("/greeter/french", new FrenchChatbot())
    .Map("/greeter/spanish", new SpanishChatbot());

await using var server = new Server(router);
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
