using IceRpc;
using Microsoft.Extensions.Logging;

using IceRpc_Slice_Client;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Debug));

// Create a client connection that logs messages to a logger with category IceRpc.ClientConnection.
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    logger: loggerFactory.CreateLogger<ClientConnection>());

// Create an invocation pipeline with two interceptors.
Pipeline pipeline = new Pipeline()
    .UseLogger(loggerFactory)
    .UseDeadline(defaultTimeout: TimeSpan.FromSeconds(10))
    .Into(connection);

// Create a greeter proxy with this invocation pipeline.
var greeterProxy = new GreeterProxy(pipeline);

string greeting = await greeterProxy.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
