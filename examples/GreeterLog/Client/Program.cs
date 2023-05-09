// Copyright (c) ZeroC, Inc.

using GreeterLogExample;
using IceRpc;
using Microsoft.Extensions.Logging;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Debug));

// Create a client connection that logs messages to a logger with category IceRpc.ClientConnection.
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    logger: loggerFactory.CreateLogger<ClientConnection>());

// Create an invocation pipeline and install the logger interceptor. This interceptor logs invocations using category
// `IceRpc.Logger.LoggerInterceptor`.
Pipeline pipeline = new Pipeline()
    .UseLogger(loggerFactory)
    .Into(connection);

// Create a greeter proxy with this invocation pipeline.
var greeterProxy = new GreeterProxy(pipeline);

string greeting = await greeterProxy.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
