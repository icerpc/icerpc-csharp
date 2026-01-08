using IceRpc;
#if (transport == "tcp")
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
#endif
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;
using IceRpc_Protobuf_Client;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Information));

// Path to the root CA certificate.
using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("certs/cacert.der");

// Create a client connection that logs messages to a logger with category IceRpc.ClientConnection.
#if (transport == "tcp")
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA),
    multiplexedClientTransport: new SlicClientTransport(new TcpClientTransport()),
    logger: loggerFactory.CreateLogger<ClientConnection>());
#else
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA),
    logger: loggerFactory.CreateLogger<ClientConnection>());
#endif

// Create an invocation pipeline with two interceptors.
Pipeline pipeline = new Pipeline()
    .UseLogger(loggerFactory)
    .UseDeadline(defaultTimeout: TimeSpan.FromSeconds(10))
    .Into(connection);

// Create a greeter client with this invocation pipeline.
var greeter = new GreeterClient(pipeline);

var request = new GreetRequest { Name = Environment.UserName };
GreetResponse response = await greeter.GreetAsync(request);

Console.WriteLine(response.Greeting);

await connection.ShutdownAsync();
