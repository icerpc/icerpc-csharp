// Copyright (c) ZeroC, Inc.

using IceRpc;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;
using VisitorCenter;

// Create a simple console logger factory and configure the log level for category IceRpc.
using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Debug));

// Load the test root CA certificate in order to connect to the server that uses a test server certificate.
using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

// Create a secure client connection to the server using the default transport (QUIC).
// This connection logs messages to a logger with category IceRpc.ClientConnection.
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA),
    logger: loggerFactory.CreateLogger<ClientConnection>());

// Create an invocation pipeline and install the logger interceptor. This interceptor logs invocations using category
// `IceRpc.Logger.LoggerInterceptor`.
Pipeline pipeline = new Pipeline()
    .UseLogger(loggerFactory)
    .Into(connection);

// Create a greeter proxy with this invocation pipeline.
var greeter = new GreeterProxy(pipeline);

string greeting = await greeter.GreetAsync(Environment.UserName);

Console.WriteLine(greeting);

await connection.ShutdownAsync();
