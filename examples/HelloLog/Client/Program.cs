// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.Logging;
using HelloLogExample;
using IceRpc;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(
    builder => builder.AddSimpleConsole().AddFilter("IceRpc", LogLevel.Trace));

// Create a logger for category `IceRpc.ClientConnection`.
ILogger logger = loggerFactory.CreateLogger<ClientConnection>();

// Create a client connection that logs messages using logger.
await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"), logger: logger);

var helloProxy = new HelloProxy(connection);
string greeting = await helloProxy.SayHelloAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
