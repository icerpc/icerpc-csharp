// Copyright (c) ZeroC, Inc.

using IceRpc;
using IceRpc.Slice;
using Igloo;
using Microsoft.Extensions.Logging;
using ThermostatServer;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Information));

using var shutdownCts = new CancellationTokenSource();

// In this simple example, the server manages a single device at a time. In a more realistic example, the server would
// manage multiple devices, each with an associated ThermoFacade instance, device connection, device pipeline and
// ThermoControlProxy instance.

var deviceConnection = new DeviceConnection();
Pipeline pipeline = new Pipeline().UseLogger(loggerFactory).Into(deviceConnection);
var thermoControlProxy = new ThermoControlProxy(pipeline);

// Front-end, client-facing.

var thermoFacade = new ThermoFacade(thermoControlProxy, shutdownCts.Token);

Router frontEndRouter = new Router()
    .UseLogger(loggerFactory)
    .Map<IThermostatService>(thermoFacade); // use default path

await using var frontEnd = new Server(frontEndRouter, logger: loggerFactory.CreateLogger<Server>());

// Back-end, device-facing.

Router backEndRouter = new Router()
    .UseLogger(loggerFactory)
    .UseDispatchInformation()
    .Map<IThermoHomeService>(new ThermoBridge(thermoFacade, deviceConnection));

await using var backEnd = new Server(
    backEndRouter,
    new Uri("icerpc://[::0]:10000"),
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
