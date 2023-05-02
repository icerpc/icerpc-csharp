using GreeterExample;
using IceRpc;
using Microsoft.Extensions.Logging;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(
    builder => builder.AddSimpleConsole().AddFilter("IceRpc", LogLevel.Trace));

// Create a logger for category `IceRpc.ClientConnection`.
ILogger logger = loggerFactory.CreateLogger<ClientConnection>();

await using var connection = new ClientConnection(new Uri("icerpc://127.0.0.1"), logger: logger);

Pipeline pipeline = new Pipeline()
    .UseLogger(logger)
    .UseDeadline(defaultTimeout: TimeSpan.FromSeconds(10))
    .Into(connection);

var greeterProxy = new GreeterProxy(pipeline);
string greeting = await greeterProxy.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
