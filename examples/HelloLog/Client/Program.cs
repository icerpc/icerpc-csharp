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

// Create a client connection with a logger created for class IceRpc.ClientConnection (it's just a recommended pattern,
// not a requirement).
await using var connection = new ClientConnection(
    new Uri("icerpc://127.0.0.1"),
    logger: loggerFactory.CreateLogger<ClientConnection>());

var helloProxy = new HelloProxy(connection);
string greeting = await helloProxy.SayHelloAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
