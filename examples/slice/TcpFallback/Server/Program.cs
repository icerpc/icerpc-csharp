// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Transports.Quic;
using Microsoft.Extensions.Logging;
using System.Net.Security;
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

// Create two servers that share the same dispatch pipeline.
await using var quicServer = new Server(
    router,
    new SslServerAuthenticationOptions
    {
        ServerCertificate = new X509Certificate2("../../../../certs/server.p12")
    },
    multiplexedServerTransport: new QuicServerTransport(),
    logger: loggerFactory.CreateLogger<Server>());

quicServer.Listen();

await using var tcpServer = new Server(
    router,
    new SslServerAuthenticationOptions
    {
        ServerCertificate = new X509Certificate2("../../../../certs/server.p12")
    },
    logger: loggerFactory.CreateLogger<Server>());

tcpServer.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await Task.WhenAll(quicServer.ShutdownAsync(), tcpServer.ShutdownAsync());
