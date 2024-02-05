// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Quic;
using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Information));

// SSL setup
using var rootCA = new X509Certificate2("../../../../certs/cacert.der");
var clientAuthenticationOptions = new SslClientAuthenticationOptions
{
    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
    {
        using var customChain = new X509Chain();
        customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        customChain.ChainPolicy.DisableCertificateDownloads = true;
        customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        customChain.ChainPolicy.CustomTrustStore.Add(rootCA);
        return customChain.Build((X509Certificate2)certificate!);
    }
};

// Use our own factory method (see below) to create a client connection using QUIC with a fallback to TCP.
await using ClientConnection connection = await CreateClientConnectionAsync(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions,
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
    Uri serverAddressUri,
    SslClientAuthenticationOptions clientAuthenticationOptions,
    ILogger logger)
{
#pragma warning disable CA2000 // Dispose objects before losing scope
    // This client connection is either returned to the caller after a successful ConnectAsync, or disposed if the
    // ConnectAsync fails.
    var quicConnection = new ClientConnection(
        serverAddressUri,
        clientAuthenticationOptions,
        multiplexedClientTransport: new QuicClientTransport(),
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
    return new ClientConnection(serverAddressUri, clientAuthenticationOptions, logger: logger);
#pragma warning restore CA2000
}
