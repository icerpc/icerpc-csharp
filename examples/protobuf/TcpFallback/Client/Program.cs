// Copyright (c) ZeroC, Inc.

using IceRpc;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Information));

using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

// Use our own factory method (see below) to create a client connection using QUIC with a fallback to TCP.
await using ClientConnection connection = await CreateClientConnectionAsync(
    new ServerAddress(new Uri("icerpc://localhost")),
    rootCA,
    loggerFactory.CreateLogger<ClientConnection>());

// Create an invocation pipeline and install the logger interceptor.
Pipeline pipeline = new Pipeline()
    .UseLogger(loggerFactory)
    .Into(connection);

// Create a greeter client with this invocation pipeline.
var greeter = new GreeterClient(pipeline);

var request = new GreetRequest { Name = Environment.UserName };
GreetResponse response = await greeter.GreetAsync(request);

Console.WriteLine(response.Greeting);

await connection.ShutdownAsync();

// Creates a client connection connected to the server over QUIC if possible. If the QUIC connection establishment
// fails, fall back to a TCP connection. The caller must dispose the returned connection.
static async Task<ClientConnection> CreateClientConnectionAsync(
    ServerAddress serverAddress,
    X509Certificate2 rootCA,
    ILogger logger)
{
#pragma warning disable CA2000 // Dispose objects before losing scope
    // This client connection is either returned to the caller after a successful ConnectAsync, or disposed if the
    // ConnectAsync fails.
    var quicConnection = new ClientConnection(
        serverAddress with { Transport = "quic" },
        clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA),
        logger: logger);
#pragma warning restore CA2000

    try
    {
        await quicConnection.ConnectAsync();
        return quicConnection;
    }
    catch (Exception exception)
    {
        logger.LogInformation(exception, "Failed to connect to server using QUIC, falling back to TCP");
        await quicConnection.DisposeAsync();
    }

#pragma warning disable CA2000 // Dispose objects before losing scope
    return new ClientConnection(
        serverAddress with { Transport = "tcp" },
        clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA),
        logger: logger);
#pragma warning restore CA2000
}
