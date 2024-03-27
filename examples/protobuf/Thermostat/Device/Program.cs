// Copyright (c) ZeroC, Inc.

using IceRpc;
using Igloo;
using Microsoft.Extensions.Logging;
using ThermostatDevice;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Debug));

var thermoBot = new ThermoBot();

Router router = new Router().UseLogger(loggerFactory).Map<IThermoControlService>(thermoBot);

// Create a client connection to the server. It dispatches requests from the server to `router`.
await using var connection = new ClientConnection(
    new ClientConnectionOptions
    {
        Dispatcher = router,
        ServerAddress = new ServerAddress(new Uri("icerpc://localhost:10000"))
    },
    logger: loggerFactory.CreateLogger<ClientConnection>());

Pipeline pipeline = new Pipeline()
    .UseDeadline(TimeSpan.FromSeconds(60))
    .UseLogger(loggerFactory)
    .Into(connection);

var thermoHome = new ThermoHomeClient(pipeline);

// Call home and stream readings.
await thermoHome.ReportAsync(thermoBot.ProduceReadingsAsync());

// Wait until the ThermoHome service stops reading.
await thermoBot.ReadCompleted;

// Graceful shutdown.
await connection.ShutdownAsync();
