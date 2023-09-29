// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Slice;
using Igloo;
using Microsoft.Extensions.Logging;
using ThermostatDevice;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Debug));

var thermoCore = new ThermoCore();

Router router = new Router()
    .UseLogger(loggerFactory)
    .Map<IThermoControlService>(thermoCore);

// Create a client connection to the cloud server. It dispatches requests to router.
await using var connection = new ClientConnection(
    new ClientConnectionOptions
    {
        Dispatcher = router,
        ServerAddress = new ServerAddress(new Uri("icerpc://localhost:10000"))
    },
    logger: loggerFactory.CreateLogger<ClientConnection>());

Pipeline pipeline = new Pipeline()
    .UseDeadline(TimeSpan.FromSeconds(3))
    .UseLogger(loggerFactory)
    .Into(connection);

var thermoHomeProxy = new ThermoHomeProxy(pipeline);

// Connect and stream readings.
await thermoHomeProxy.ReportAsync(thermoCore.ReadAsync());

// Wait until the ThermoHome service stops reading, then exits.
await thermoCore.ReadCompleted;
