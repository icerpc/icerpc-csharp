using IceRpc;
using IceRpc.Slice;
#if (transport == "quic")
using IceRpc.Transports.Quic;
#endif
using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

using IceRpc_Slice_Server;

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

var sslServerAuthenticationOptions = new SslServerAuthenticationOptions
{
    ServerCertificateContext = SslStreamCertificateContext.Create(
        X509CertificateLoader.LoadPkcs12FromFile(
            "certs/server.p12",
            password: null,
            keyStorageFlags: X509KeyStorageFlags.Exportable),
        additionalCertificates: null)
};

// Create a server that logs message to a logger with category `IceRpc.Server`.
#if (transport == "quic")
await using var server = new Server(
    router,
    sslServerAuthenticationOptions,
    multiplexedServerTransport: new QuicServerTransport(),
    logger: loggerFactory.CreateLogger<Server>());
#else
await using var server = new Server(
    router,
    sslServerAuthenticationOptions,
    logger: loggerFactory.CreateLogger<Server>());
#endif

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
