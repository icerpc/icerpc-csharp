// Copyright (c) ZeroC, Inc.

using DiscriminatedUnionServer;
using IceRpc;

// Create a server that dispatches all requests to the same service, an instance of MathWizard.
await using var server = new Server(new MathWizard());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
