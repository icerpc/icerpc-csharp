using IceRpc;
using IceRpc.Protobuf;
using Microsoft.Extensions.Logging;

using IceRpc_Protobuf_Server;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Debug));

// Create a router (dispatch pipeline), install two middleware and map our implementation of `IGreeterService` at the
// default path for this interface: `/VisitorCenter.Greeter`
Router router = new Router()
    .UseLogger(loggerFactory)
    .UseDeadline()
    .Map<IGreeterService>(new Chatbot());

// Create a server that logs message to a logger with category `IceRpc.Server`.
await using var server = new Server(router, logger: loggerFactory.CreateLogger<Server>());

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
