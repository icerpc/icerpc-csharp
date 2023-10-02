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

var thermoBot = new ThermoBot();

Router router = new Router().UseLogger(loggerFactory).Map<IThermoControlService>(thermoBot);

// Create a client connection to the server. It dispatches requests to router.
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

// Call home and stream readings.
await thermoHomeProxy.ReportAsync(thermoBot.ProduceReadingsAsync());

// Wait until the ThermoHome service stops reading, then exits.
await thermoBot.ReadCompleted;
