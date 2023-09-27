// Copyright (c) ZeroC, Inc.

using Abode;
using IceRpc;
using IceRpc.Slice;
using Microsoft.Extensions.Logging;
using ThermostatDevice;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Debug));

Router router = new Router()
    .UseLogger(loggerFactory)
    .Map<IThermostatService>(new FakeDevice());

await using var server = new Server(router, logger: loggerFactory.CreateLogger<Server>());

server.Listen();

// Wait until the console receives a Ctrl+C.
await CancelKeyPressed;
await server.ShutdownAsync();
