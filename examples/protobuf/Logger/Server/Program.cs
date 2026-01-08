// Copyright (c) ZeroC, Inc.

using IceRpc;
using LoggerServer;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Debug));

// Load the server certificate.
using var serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "../../../../certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

// Create a router (dispatch pipeline) and install the logger middleware. This middleware logs dispatches using category
// `IceRpc.Logger.LoggerMiddleware`.
Router router = new Router()
    .UseLogger(loggerFactory)
    .Map<IGreeterService>(new Chatbot());

// Create a server that logs message to a logger with category `IceRpc.Server`.
await using var server = new Server(
    router,
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate),
    logger: loggerFactory.CreateLogger<Server>());

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
