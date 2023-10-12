// Copyright (c) ZeroC, Inc.

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

var thermostat = new thermostat(pipeline);

// This client provides two commands: monitor and set.
// monitor is the default and streams readings until your press Ctrl+C.
// set <value> changes the set point and exits.

var monitorCommand = new Command("monitor", "Monitor the thermostat (default)");
monitorCommand.SetHandler(MonitorAsync);

var setCommand = new Command("set", "Change the set point");
var argument = new Argument<float>("setPoint");
setCommand.AddArgument(argument);
setCommand.SetHandler(ChangeSetPointAsync, argument);

var rootCommand = new RootCommand()
{
    monitorCommand,
    setCommand
};

rootCommand.SetHandler(MonitorAsync);

await rootCommand.InvokeAsync(args);

// Graceful shutdown
await connection.ShutdownAsync();

async Task ChangeSetPointAsync(float setPoint)
{
    try
    {
        await thermostat.ChangeSetPointAsync(setPoint);
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Successfully changed set point to {setPoint}°F.");
    }
    catch (DispatchException exception)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed to change set point to {setPoint}°F: StatusCode = {exception.StatusCode}, Message = {exception.Message}");
    }
    catch (Exception exception)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Failed to change set point to {setPoint}°F: {exception}");
    }
    Console.ResetColor();
}

async Task MonitorAsync()
{
    // We cancel cts when the user presses Ctrl+C.
    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (sender, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cts.Cancel();
    };

    IAsyncEnumerable<Reading> readings = await thermostat.MonitorAsync();

    // The iteration completes when cts is canceled.
    await foreach (Reading reading in readings.WithCancellation(cts.Token))
    {
        Console.WriteLine(reading);
    }
}
