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

var thermoFacade = new ThermoFacade();
var thermoBridge = new ThermoBridge(thermoFacade);

Router frontEndRouter = new Router()
    .UseLogger(loggerFactory)
    .Map<IThermostatService>(thermoFacade);

Router backEndRouter = new Router()
    .UseLogger(loggerFactory)
    .UseDispatchInformation()
    .Map<IThermoHomeService>(thermoBridge);

await using var backEnd = new Server(
    backEndRouter,
    new Uri("icerpc://[::0]:10000"),
    logger: loggerFactory.CreateLogger<Server>());

await using var frontEnd = new Server(frontEndRouter, logger: loggerFactory.CreateLogger<Server>());

backEnd.Listen();
frontEnd.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await frontEnd.ShutdownAsync();
await backEnd.ShutdownAsync();
