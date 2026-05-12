using IceRpc;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;

using IceRpc_Ice_Server;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Information));

// Create a router (dispatch pipeline), install two middleware and map our implementation of `IGreeterService` at the
// Ice path `/greeter`.
Router router = new Router()
    .UseLogger(loggerFactory)
    .UseDeadline()
    .Map("/greeter", new Chatbot());

using X509Certificate2 serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

// Create a server that uses the ice protocol for compatibility with ZeroC Ice, and logs messages to a
// logger with category `IceRpc.Server`. The default port for the ice protocol is 4061.
await using var server = new Server(
    router,
    serverAddressUri: new Uri("ice://[::0]"),
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate),
    logger: loggerFactory.CreateLogger<Server>());

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
