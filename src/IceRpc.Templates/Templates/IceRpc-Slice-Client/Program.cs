using IceRpc;
#if (transport == "quic")
using IceRpc.Transports.Quic;
#endif
using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using IceRpc_Slice_Client;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Information));

// Path to the root CA certificate.
using var rootCA = X509CertificateLoader.LoadCertificateFromFile("certs/cacert.der");

// Create Client authentication options with custom certificate validation.
var clientAuthenticationOptions = new SslClientAuthenticationOptions
{
    RemoteCertificateValidationCallback = (sender, certificate, chain, errors) =>
    {
        if (certificate is X509Certificate2 peerCertificate)
        {
            using var customChain = new X509Chain();
            customChain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            customChain.ChainPolicy.DisableCertificateDownloads = true;
            customChain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            customChain.ChainPolicy.CustomTrustStore.Add(rootCA);
            return customChain.Build(peerCertificate);
        }
        else
        {
            return false;
        }
    }
};

#if (transport == "quic")
// Create a client connection that logs messages to a logger with category IceRpc.ClientConnection.
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions,
    multiplexedClientTransport: new QuicClientTransport(),
    logger: loggerFactory.CreateLogger<ClientConnection>());
#else
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions,
    logger: loggerFactory.CreateLogger<ClientConnection>());
#endif

// Create an invocation pipeline with two interceptors.
Pipeline pipeline = new Pipeline()
    .UseLogger(loggerFactory)
    .UseDeadline(defaultTimeout: TimeSpan.FromSeconds(10))
    .Into(connection);

// Create a greeter proxy with this invocation pipeline.
var greeter = new GreeterProxy(pipeline);

string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
