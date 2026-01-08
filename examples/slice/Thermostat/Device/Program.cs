// Copyright (c) ZeroC, Inc.

using IceRpc;
using Igloo;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;
using ThermostatDevice;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Debug));

// Load the test root CA certificate in order to connect to the server that uses a test server certificate.
using X509Certificate2 rootCA = X509CertificateLoader.LoadCertificateFromFile("../../../../certs/cacert.der");

var thermoBot = new ThermoBot();

Router router = new Router().UseLogger(loggerFactory).Map<IThermoControlService>(thermoBot);

// Create a secure client connection to the server using the default transport (QUIC).
// It dispatches requests from the server to `router`.
await using var connection = new ClientConnection(
    new ClientConnectionOptions
    {
        Dispatcher = router,
        ServerAddress = new ServerAddress(new Uri("icerpc://localhost:10000")),
        ClientAuthenticationOptions = CreateClientAuthenticationOptions(rootCA)
    },
    logger: loggerFactory.CreateLogger<ClientConnection>());

Pipeline pipeline = new Pipeline()
    .UseDeadline(TimeSpan.FromSeconds(3))
    .UseLogger(loggerFactory)
    .Into(connection);

var thermoHome = new ThermoHomeProxy(pipeline);

// Call home and stream readings.
await thermoHome.ReportAsync(thermoBot.ProduceReadingsAsync());

// Wait until the ThermoHome service stops reading.
await thermoBot.ReadCompleted;

// Graceful shutdown.
await connection.ShutdownAsync();
