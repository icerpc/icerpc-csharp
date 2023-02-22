// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using HelloLogExample;
using IceRpc;

// Create a simple console logger factory and configure the log level for category IceRpc.
using var loggerFactory = LoggerFactory.Create(
    builder => builder
        .AddSimpleConsole(configure => configure.IncludeScopes = true)
        .AddFilter("IceRpc", LogLevel.Trace));

// Create a server with a logger created for class IceRpc.Server (it's just a recommended pattern, not a requirement).
await using var server = new Server(new Chatbot(), logger: loggerFactory.CreateLogger<Server>());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
