// Copyright (c) ZeroC, Inc.

using Microsoft.Extensions.Logging;
using GreeterLogExample;
using IceRpc;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(
    builder => builder.AddSimpleConsole().AddFilter("IceRpc", LogLevel.Trace));

// Create a logger for category `IceRpc.ClientConnection`.
ILogger logger = loggerFactory.CreateLogger<ClientConnection>();

// Create a client connection that logs messages using logger.
await using var connection = new ClientConnection(new Uri("icerpc://localhost"), logger: logger);

var greeterProxy = new GreeterProxy(connection);
string greeting = await greeterProxy.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
