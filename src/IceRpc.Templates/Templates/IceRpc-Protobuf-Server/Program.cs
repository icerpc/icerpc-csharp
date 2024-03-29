using IceRpc;
using IceRpc.Protobuf;
#if (transport == "quic")
using IceRpc.Transports.Quic;
#endif
using Microsoft.Extensions.Logging;
#if (transport == "quic")
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
#endif

using IceRpc_Protobuf_Server;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Information));

// Create a router (dispatch pipeline), install two middleware and map our implementation of `IGreeterService` at the
// default path for this interface: `/VisitorCenter.Greeter`
Router router = new Router()
    .UseLogger(loggerFactory)
    .UseDeadline()
    .Map<IGreeterService>(new Chatbot());

// Create a server that logs message to a logger with category `IceRpc.Server`.
#if (transport == "quic")
var sslServerAuthenticationOptions = new SslServerAuthenticationOptions
{
    ServerCertificate = new X509Certificate2("certs/server.p12")
};

await using var server = new Server(
    router,
    sslServerAuthenticationOptions,
    multiplexedServerTransport: new QuicServerTransport(),
    logger: loggerFactory.CreateLogger<Server>());
#else
await using var server = new Server(router, logger: loggerFactory.CreateLogger<Server>());
#endif

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
