// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Quic;
using Microsoft.Extensions.Logging;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using TcpFallbackServer;
using VisitorCenter;

const string serverCert = "../../../../certs/server_cert.pem";
const string serverKey = "../../../../certs/server_key.pem";

// The X509 certificate used by the server.
using var serverCertificate = X509Certificate2.CreateFromPemFile(serverCert, serverKey);

// Create a collection with the server certificate and any intermediate certificates. This is used by
// ServerCertificateContext to provide the certificate chain to the peer.
var intermediates = new X509Certificate2Collection();
intermediates.ImportFromPemFile(serverCert);

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Debug));

// Create a router (dispatch pipeline) with the greeter service.
Router router = new Router()
    .Map<IGreeterService>(new Chatbot());

// Create two servers that share the same dispatch pipeline.
await using var quicServer = new Server(
    router,
    new SslServerAuthenticationOptions
    {
        ServerCertificateContext = SslStreamCertificateContext.Create(serverCertificate, intermediates)
    },
    multiplexedServerTransport: new QuicServerTransport(),
    logger: loggerFactory.CreateLogger<Server>());

quicServer.Listen();

await using var tcpServer = new Server(
    router,
    new SslServerAuthenticationOptions
    {
        ServerCertificateContext = SslStreamCertificateContext.Create(serverCertificate, intermediates)
    },
    logger: loggerFactory.CreateLogger<Server>());

tcpServer.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await Task.WhenAll(quicServer.ShutdownAsync(), tcpServer.ShutdownAsync());
