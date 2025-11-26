// Copyright (c) ZeroC, Inc.

using Google.Protobuf.WellKnownTypes;
using IceRpc;
using Igloo;
using Microsoft.Extensions.Logging;
using System.CommandLine;

using ILoggerFactory loggerFactory = LoggerFactory.Create(builder =>
    builder
        .AddSimpleConsole()
        .AddFilter("IceRpc", LogLevel.Information));

// Create a client connection to Server.
await using var connection = new ClientConnection(
    new Uri("icerpc://localhost"),
    logger: loggerFactory.CreateLogger<ClientConnection>());

Pipeline pipeline = new Pipeline()
    .UseDeadline(TimeSpan.FromSeconds(3))
    .UseLogger(loggerFactory)
    .Into(connection);

var thermostat = new ThermostatClient(pipeline);

// This client provides two commands: monitor and set.
// monitor is the default and streams readings until your press Ctrl+C.
// set <value> changes the set point and exits.

var monitorCommand = new Command("monitor", "Monitor the thermostat (default)");
monitorCommand.SetAction(
    async (parseResult, cancellationToken) => await MonitorAsync(cancellationToken));

var setCommand = new Command("set", "Change the set point");
var argument = new Argument<float>("setPoint");
setCommand.Arguments.Add(argument);
setCommand.SetAction(
    async (parseResult, cancellationToken) =>
    {
        float value = parseResult.GetRequiredValue(argument);
        await ChangeSetPointAsync(value, cancellationToken);
    });

var rootCommand = new RootCommand()
{
    monitorCommand,
    setCommand
};
rootCommand.Action = monitorCommand.Action;

await rootCommand.Parse(args).InvokeAsync();

// Graceful shutdown
await connection.ShutdownAsync();

async Task ChangeSetPointAsync(float value, CancellationToken cancellationToken)
{
    try
    {
        await thermostat.ChangeSetPointAsync(
            new ChangeSetPointRequest { SetPoint = value },
            cancellationToken: cancellationToken);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Successfully changed set point to {value}°F.");
    }
    catch (DispatchException exception)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(
            $"Failed to change set point to {value}°F: StatusCode = {exception.StatusCode}, Message = {exception.Message}");
    }
    catch (Exception exception)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed to change set point to {value}°F: {exception}");
    }
    Console.ResetColor();
}

async Task MonitorAsync(CancellationToken cancellationToken)
{
    IAsyncEnumerable<Reading> readings = await thermostat.MonitorAsync(
        new Empty(),
        cancellationToken: cancellationToken);

    // The iteration completes when cancellationToken is canceled.
    await foreach (Reading reading in readings.WithCancellation(cancellationToken))
    {
        Console.WriteLine(reading);
    }
}
