// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Slic;
using IceRpc.Transports.Tcp;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;
using TcpFallbackServer;
using VisitorCenter;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Debug));

// Create a router (dispatch pipeline) with the greeter service.
Router router = new Router()
    .Map<IGreeterService>(new Chatbot());

using X509Certificate2 serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "../../../../certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

// Create two servers that share the same dispatch pipeline.
await using var quicServer = new Server(
    router,
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate),
    logger: loggerFactory.CreateLogger<Server>());

quicServer.Listen();

await using var tcpServer = new Server(
    router,
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate),
    multiplexedServerTransport: new SlicServerTransport(new TcpServerTransport()),
    logger: loggerFactory.CreateLogger<Server>());

tcpServer.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await Task.WhenAll(quicServer.ShutdownAsync(), tcpServer.ShutdownAsync());
