// Copyright (c) ZeroC, Inc.

using IceRpc;
using Igloo;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;
using ThermostatServer;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Information));

// The default transport (QUIC) requires a server certificate. We use a test certificate here.
using X509Certificate2 serverCertificate = X509CertificateLoader.LoadPkcs12FromFile(
    "../../../../certs/server.p12",
    password: null,
    keyStorageFlags: X509KeyStorageFlags.Exportable);

using var shutdownCts = new CancellationTokenSource();

// In this simple example, the server manages a single device at a time. In a more realistic example, the server would
// manage multiple devices, each with an associated ThermoFacade instance, device connection, device pipeline and
// ThermoControlProxy instance.

var deviceConnection = new DeviceConnection();
Pipeline pipeline = new Pipeline().UseLogger(loggerFactory).Into(deviceConnection);
var thermoControl = new ThermoControlProxy(pipeline);

// Front-end, client-facing.

var thermoFacade = new ThermoFacade(thermoControl, shutdownCts.Token);

Router frontEndRouter = new Router()
    .UseLogger(loggerFactory)
    .Map<IThermostatService>(thermoFacade); // use default path

await using var frontEnd = new Server(
    frontEndRouter,
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate),
    logger: loggerFactory.CreateLogger<Server>());

// Back-end, device-facing.

Router backEndRouter = new Router()
    .UseLogger(loggerFactory)
    .UseDispatchInformation()
    .Map<IThermoHomeService>(new ThermoBridge(thermoFacade, deviceConnection));

await using var backEnd = new Server(
    backEndRouter,
    new Uri("icerpc://[::0]:10000"),
    serverAuthenticationOptions: CreateServerAuthenticationOptions(serverCertificate),
    logger: loggerFactory.CreateLogger<Server>());

// Start

backEnd.Listen();
frontEnd.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;

// Cancel streaming by outstanding dispatches.
shutdownCts.Cancel();

await frontEnd.ShutdownAsync();
await backEnd.ShutdownAsync();
