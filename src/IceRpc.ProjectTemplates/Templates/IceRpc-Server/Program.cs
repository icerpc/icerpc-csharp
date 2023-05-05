using IceRpc;
using Microsoft.Extensions.Logging;

using IceRpc_server;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(
    builder => builder.AddSimpleConsole().AddFilter("IceRpc", LogLevel.Trace));

Router router = new Router()
    .UseLogger(loggerFactory)
    .UseDeadline()
    .Map<IGreeterService>(new Chatbot());

await using var server = new Server(
    dispatcher: router,
    serverAuthenticationOptions: null,
    logger: loggerFactory.CreateLogger<Server>());
server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
