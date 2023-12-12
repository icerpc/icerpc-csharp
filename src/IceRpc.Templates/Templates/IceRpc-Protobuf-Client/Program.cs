using IceRpc;
using Microsoft.Extensions.Logging;

using IceRpc_Protobuf_Client;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Information));

// Create a client connection that logs messages to a logger with category IceRpc.ClientConnection.
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    logger: loggerFactory.CreateLogger<ClientConnection>());

// Create an invocation pipeline with two interceptors.
Pipeline pipeline = new Pipeline()
    .UseLogger(loggerFactory)
    .UseDeadline(defaultTimeout: TimeSpan.FromSeconds(10))
    .Into(connection);

// Create a greeter client with this invocation pipeline.
var greeter = new GreeterClient(pipeline);

var request = new GreetRequest { Name = Environment.UserName };
GreetResponse response= await greeter.GreetAsync(request);

Console.WriteLine(response.Greeting);

await connection.ShutdownAsync();
