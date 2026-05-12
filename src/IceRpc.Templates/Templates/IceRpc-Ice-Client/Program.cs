using IceRpc;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Information));

// Path to the root CA certificate.
using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("certs/cacert.der");

// Create a client connection that logs messages to a logger with category IceRpc.ClientConnection.
// We use the ice protocol for compatibility with ZeroC Ice. The default port for the ice protocol is 4061.
await using var connection = new ClientConnection(
    new Uri("ice://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA),
    logger: loggerFactory.CreateLogger<ClientConnection>());

// Create an invocation pipeline with two interceptors.
Pipeline pipeline = new Pipeline()
    .UseLogger(loggerFactory)
    .UseDeadline(defaultTimeout: TimeSpan.FromSeconds(10))
    .Into(connection);

// Create a greeter proxy with this invocation pipeline. The service address URI includes the protocol
// to use (ice).
var greeter = new GreeterProxy(pipeline, new Uri("ice:/greeter"));

string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
