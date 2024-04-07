using IceRpc;
#if (transport == "quic")
using IceRpc.Transports.Quic;
#endif
using Microsoft.Extensions.Logging;
#if (transport == "quic")
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
#endif

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

// Create a server that logs message to a logger with category `IceRpc.Server`.
#if (transport == "quic")
const string serverCert = "certs/server_cert.pem";
const string serverKey = "certs/server_key.pem";

// The X509 certificate used by the server.
using var serverCertificate = X509Certificate2.CreateFromPemFile(serverCert, serverKey);

// Create a collection with the server certificate and any intermediate certificates. This is used by
// ServerCertificateContext to provide the certificate chain to the peer.
var intermediates = new X509Certificate2Collection();
intermediates.ImportFromPemFile(serverCert);

var sslServerAuthenticationOptions = new SslServerAuthenticationOptions
{
    ServerCertificateContext = SslStreamCertificateContext.Create(serverCertificate, intermediates)
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
