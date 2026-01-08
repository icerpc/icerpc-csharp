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

// Load the root CA certificate.
using var rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

// Create a client connection that logs messages to a logger with category IceRpc.ClientConnection.
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    clientAuthenticationOptions: CreateClientAuthenticationOptions(rootCA),
    logger: loggerFactory.CreateLogger<ClientConnection>());

// Create an invocation pipeline and install the logger interceptor. This interceptor logs invocations using category
// `IceRpc.Logger.LoggerInterceptor`.
Pipeline pipeline = new Pipeline()
    .UseLogger(loggerFactory)
    .Into(connection);

// Create a greeter client with this invocation pipeline.
var greeter = new GreeterClient(pipeline);

var request = new GreetRequest { Name = Environment.UserName };
GreetResponse response = await greeter.GreetAsync(request);

Console.WriteLine(response.Greeting);

await connection.ShutdownAsync();
