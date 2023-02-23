// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.Logging;
using HelloLogExample;
using IceRpc;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(
    builder => builder.AddSimpleConsole().AddFilter("IceRpc", LogLevel.Trace));

// Create a logger for category `IceRpc.Server`.
ILogger logger = loggerFactory.CreateLogger<Server>();

// Create a server that logs messages using logger.
await using var server = new Server(new Chatbot(), logger: logger);

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
